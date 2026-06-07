using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PgPlanCheck;

/// <summary>インデックス非効率（sargable でない）な書き方の種別。</summary>
public enum SargabilityIssueKind
{
    /// <summary>列への型キャスト（例: <c>(customer_id)::text = '123'</c>）。</summary>
    ColumnCast,
    /// <summary>列への関数適用（例: <c>lower(name) = 'x'</c>）。</summary>
    FunctionOnColumn,
    /// <summary>先頭ワイルドカードの LIKE（例: <c>note ~~ '%abc%'</c>）。</summary>
    LeadingWildcard,
}

/// <summary>検出した 1 件のインデックス非効率（対象列つき）。</summary>
public readonly record struct SargabilityIssue(SargabilityIssueKind Kind, string Column);

/// <summary>
/// EXPLAIN の Filter / Index Cond 文字列から、比較対象の列名を
/// ベストエフォートで抽出する。インデックス提案や sargability 判定の材料にする。
/// </summary>
public static class FilterColumnExtractor
{
    // 「（省略可能な 別名/スキーマ.）列名」の直後に比較演算子が続くパターン。
    // 例: orders.status = ... / amount > ... のいずれも列名(status, amount)を group1 で取得する。
    private static readonly Regex ColumnBeforeOperator = new(
        @"(?<![\w.])(?:[A-Za-z_][A-Za-z0-9_]*\.)?([A-Za-z_][A-Za-z0-9_]*)\s*(?:=|<>|!=|<=|>=|<|>|~~\*?|!~~\*?|~\*?|IS\b|IN\b|BETWEEN\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 列名と誤認しやすいキーワード・型名は除外する。
    private static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "or", "not", "null", "true", "false", "is", "in", "between", "like", "any", "all",
        "text", "varchar", "char", "character", "varying", "integer", "int", "int4", "int8",
        "bigint", "smallint", "numeric", "decimal", "real", "double", "precision", "float",
        "boolean", "bool", "date", "time", "timestamp", "timestamptz", "interval", "json", "jsonb",
        "uuid", "bytea", "money"
    };

    public static List<string> Extract(string? condition)
    {
        var cols = new List<string>();
        if (string.IsNullOrWhiteSpace(condition)) return cols;

        foreach (Match m in ColumnBeforeOperator.Matches(condition))
        {
            var name = m.Groups[1].Value;
            if (Stop.Contains(name)) continue;
            if (!cols.Contains(name)) cols.Add(name);
        }
        return cols;
    }

    /// <summary>条件式に関数呼び出し（例: lower(name)）が含まれていそうかの簡易判定。</summary>
    public static bool LooksLikeExpression(string? condition)
    {
        if (string.IsNullOrWhiteSpace(condition)) return false;
        // 「英字( … )」の形があり、かつ素の列比較が拾えていない場合に式インデックスを示唆する。
        return Regex.IsMatch(condition, @"[A-Za-z_][A-Za-z0-9_]*\s*\(");
    }

    // ---- B2: 述語の種別（等値/範囲）分類。複合索引の列順最適化に使う ----

    /// <summary>条件から等値述語列・範囲述語列を分類した結果。</summary>
    public readonly record struct PredicateColumns(List<string> Equality, List<string> Range);

    // 列 + 演算子。複数文字演算子（<=,>= など）を 1 文字より先に並べる。
    private static readonly Regex ColumnWithOperator = new(
        @"(?<![\w.])(?:[A-Za-z_][A-Za-z0-9_]*\.)?([A-Za-z_][A-Za-z0-9_]*)\s*(<=|>=|<>|!=|=|<|>|~~\*?|\bIN\b|\bIS\b|\bBETWEEN\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Filter から等値列（= / IN / IS）と範囲列（&lt; &gt; &lt;= &gt;= BETWEEN）を分類する。</summary>
    public static PredicateColumns ClassifyPredicates(string? condition)
    {
        var eq = new List<string>();
        var range = new List<string>();
        if (string.IsNullOrWhiteSpace(condition)) return new PredicateColumns(eq, range);

        foreach (Match m in ColumnWithOperator.Matches(condition))
        {
            var col = m.Groups[1].Value;
            if (Stop.Contains(col)) continue;
            var op = m.Groups[2].Value.ToUpperInvariant();

            if (op is "=" or "IN" or "IS")
            {
                if (!eq.Contains(col)) eq.Add(col);
            }
            else if (op is "<" or ">" or "<=" or ">=" or "BETWEEN")
            {
                if (!range.Contains(col)) range.Add(col);
            }
            // <>, !=, ~~ は索引順序に寄与しないため無視
        }

        range.RemoveAll(c => eq.Contains(c)); // 同一列は等値を優先
        return new PredicateColumns(eq, range);
    }

    // ---- A3: sargability（インデックス非効率）検出 ----

    // 列への型キャスト。(col)::type / col::type を拾う。リテラルのキャスト 'x'::type は
    // 直前が引用符のため一致しない（= 列が型変換されている場合だけ検出）。
    private static readonly Regex ColumnCastRe = new(
        @"\(?\b([A-Za-z_][A-Za-z0-9_]*)\)?::[A-Za-z_]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 関数呼び出しの先頭引数が列。例: lower(name) / date(created_at)。
    private static readonly Regex FunctionCallRe = new(
        @"\b([A-Za-z_][A-Za-z0-9_]*)\s*\(\s*\(?\s*(?:[A-Za-z_][A-Za-z0-9_]*\.)?([A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 先頭ワイルドカードの LIKE/ILIKE。col ~~ '%...' / col ~~* '_...'。NOT LIKE(!~~) は除外。
    private static readonly Regex LeadingWildcardRe = new(
        @"(?:[A-Za-z_][A-Za-z0-9_]*\.)?([A-Za-z_][A-Za-z0-9_]*)\s*(?<![!])~~\*?\s*'(?:%|_)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 関数名として現れても列への関数適用とはみなさないもの（論理/型キャスト関数形など）。
    private static readonly HashSet<string> FunctionStop = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "or", "not", "any", "all", "in", "is", "case", "when", "then", "else", "end", "exists",
        "text", "int4", "int8", "numeric", "date", "timestamp", "timestamptz", "bool", "boolean",
        "char", "varchar", "bpchar",
    };

    /// <summary>Filter 文字列からインデックス非効率な書き方を検出する。</summary>
    public static List<SargabilityIssue> DetectSargability(string? condition)
    {
        var issues = new List<SargabilityIssue>();
        if (string.IsNullOrWhiteSpace(condition)) return issues;

        foreach (Match m in ColumnCastRe.Matches(condition))
        {
            var col = m.Groups[1].Value;
            if (Stop.Contains(col)) continue;
            Add(issues, SargabilityIssueKind.ColumnCast, col);
        }

        foreach (Match m in FunctionCallRe.Matches(condition))
        {
            var func = m.Groups[1].Value;
            var arg = m.Groups[2].Value;
            if (FunctionStop.Contains(func) || Stop.Contains(arg)) continue;
            Add(issues, SargabilityIssueKind.FunctionOnColumn, arg);
        }

        foreach (Match m in LeadingWildcardRe.Matches(condition))
        {
            var col = m.Groups[1].Value;
            if (Stop.Contains(col)) continue;
            Add(issues, SargabilityIssueKind.LeadingWildcard, col);
        }

        return issues;
    }

    private static void Add(List<SargabilityIssue> list, SargabilityIssueKind kind, string col)
    {
        if (!list.Any(x => x.Kind == kind && string.Equals(x.Column, col, StringComparison.OrdinalIgnoreCase)))
            list.Add(new SargabilityIssue(kind, col));
    }
}
