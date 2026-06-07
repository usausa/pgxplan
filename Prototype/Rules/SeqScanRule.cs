using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PgPlanCheck.Catalog;

namespace PgPlanCheck.Rules;

/// <summary>
/// フィルタ付きの全件走査（Seq Scan）を検出し、インデックス作成などを提案する。
/// カタログ（B1）が利用可能なら既存インデックスとの重複を避け、列統計で選択度を補足する。
/// </summary>
public sealed class SeqScanRule : IPlanRule
{
    public string Id => "seq-scan";

    private const double TotalRows = 10_000;            // この行数以上を走査する Seq Scan を対象に
    private const double CriticalRows = 1_000_000;
    private const double SelectiveRemovedFraction = 0.5; // フィルタで半分以上を捨てていれば「選択的」
    private const double LowCardinalityNDistinct = 50;    // n_distinct がこの値以下なら低選択度とみなす

    public IEnumerable<Finding> Analyze(PlanContext ctx)
    {
        foreach (var nr in ctx.Nodes)
        {
            var node = nr.Node;
            if (node.NodeType != "Seq Scan" || node.Filter is null) continue;
            var finding = Evaluate(node, ctx);
            if (finding is not null) yield return finding;
        }
    }

    private static Finding? Evaluate(PlanNode node, PlanContext ctx)
    {
        if (!ctx.Analyzed)
        {
            // 実測なし：推定ベースで弱めに示唆
            return Build(node, ctx, Severity.Info, new List<string>
            {
                $"フィルタ: {node.Filter}",
                $"推定出力行数: {node.PlanRows:N0}（実測なし。検証専用モード）",
            });
        }

        double loops = node.Loops;
        double outRows = node.ActualRows ?? 0;
        double removed = node.RowsRemovedByFilter ?? 0;
        double scannedPerLoop = outRows + removed;
        if (scannedPerLoop <= 0) return null;

        double totalScanned = scannedPerLoop * loops;
        double removedFrac = removed / scannedPerLoop;
        if (totalScanned < TotalRows || removedFrac < SelectiveRemovedFraction) return null;

        var sev = totalScanned >= CriticalRows ? Severity.Critical : Severity.Warning;
        var details = new List<string>
        {
            $"フィルタ: {node.Filter}",
            $"走査行数: {totalScanned:N0} 行 → 出力 {outRows * loops:N0} 行",
            $"フィルタで除外: {removed * loops:N0} 行 ({removedFrac:P1})",
        };
        if (loops > 1) details.Add($"実行回数(loops): {loops:N0}");
        return Build(node, ctx, sev, details);
    }

    private static Finding Build(PlanNode node, PlanContext ctx, Severity sev, List<string> details)
    {
        const string title = "シーケンシャルスキャン（インデックス未使用の全件走査）";

        // インデックス非効率（型キャスト/関数/先頭ワイルドカード）が原因なら、単純な索引追加では効かない。
        // 詳細は SargabilityRule(A3) が別途指摘するため、ここでは索引提案を出さず委譲する。
        if (FilterColumnExtractor.DetectSargability(node.Filter).Count > 0)
        {
            return new Finding
            {
                Severity = sev,
                Title = title,
                NodeLabel = node.Label,
                Details = details,
                Suggestion = "フィルタ条件がインデックス非効率な書き方（型キャスト・列への関数適用・先頭ワイルドカード）のため、"
                           + "単純なインデックス追加では解消しません。別掲の「インデックス非効率」の指摘を参照してください。",
                SuggestedSql = null,
            };
        }

        var cols = FilterColumnExtractor.Extract(node.Filter);
        string suggestion;
        string? sql = null;

        if (cols.Count > 0 && node.RelationName is not null)
        {
            var existing = ctx.Catalog.GetIndexes(node.Schema, node.RelationName)
                .FirstOrDefault(ix => IsLeadingPrefix(ix.Columns, cols));

            if (existing is not null)
            {
                // B1: 既に対象列を先頭に持つインデックスがある → 重複提案を避け、原因を示唆する。
                suggestion = $"フィルタ列（{string.Join(", ", cols)}）を先頭に持つインデックス {existing.Name} は既に存在します。"
                           + "それでも全件走査が選ばれているのは、選択度が低い・統計が古い・条件がインデックス非効率、などの可能性があります。"
                           + "ANALYZE の実行や条件の見直しを検討してください。";
            }
            else
            {
                // B2: 等値 → 範囲 → ORDER BY の順でキー列を並べ、出力列は INCLUDE 候補に分離する。
                var orderBy = ctx.Ancestors(node)
                    .FirstOrDefault(a => a.NodeType is "Sort" or "Incremental Sort")?.SortKey;
                var (key, include) = CompositeIndexAdvisor.BuildColumns(node.Filter, orderBy, node.Output);
                if (key.Count == 0) key = cols; // 念のためのフォールバック

                var idxName = $"idx_{node.RelationName}_{string.Join("_", key)}";
                sql = $"CREATE INDEX {idxName} ON {node.QualifiedRelation} ({string.Join(", ", key)});";
                suggestion = $"フィルタ列（{string.Join(", ", cols)}）に一致するインデックスが無いため全件走査になっています。"
                           + "インデックスを作成すると必要な行だけを読めます。";
                if (orderBy is { Count: > 0 })
                    suggestion += " ORDER BY 列もキーに含めるとソートも回避できます。";

                // B1: 列統計が分かれば選択度を補足し、低選択度の等値条件には部分インデックスも提示する。
                var st = ctx.Catalog.GetColumnStats(node.Schema, node.RelationName, key[0]);
                if (st?.NDistinct is double nd)
                {
                    details.Add($"列 {key[0]} の統計: n_distinct={FormatNDistinct(nd)}"
                              + (st.Correlation is double c ? $", correlation={c:N2}" : ""));
                    if (nd > 0 && nd <= LowCardinalityNDistinct && TryGetEqualityLiteral(node.Filter!, key[0], out var lit))
                    {
                        sql += $"\n-- 補足: {key[0]} は個別値が少なく低選択度です。特定値の抽出が多いなら部分インデックスも有効:"
                             + $"\n--   CREATE INDEX {idxName}_partial ON {node.QualifiedRelation} ({key[0]}) WHERE {key[0]} = {lit};";
                    }
                }

                // B2: 出力列が少数なら Index-Only 化のための INCLUDE をコメントで提示する。
                if (include.Count is > 0 and <= 3)
                    sql += $"\n-- Index-Only 化するなら出力列を含める:"
                         + $"\n--   CREATE INDEX {idxName}_io ON {node.QualifiedRelation} ({string.Join(", ", key)}) INCLUDE ({string.Join(", ", include)});";
            }
        }
        else if (FilterColumnExtractor.LooksLikeExpression(node.Filter))
        {
            suggestion = "フィルタが関数・式を含むため通常のインデックスが効きません。"
                       + "式インデックス（例: CREATE INDEX ... ON t ((lower(col)))）を検討してください。";
        }
        else
        {
            suggestion = "フィルタ条件に一致するインデックスの作成を検討してください。";
        }

        return new Finding
        {
            Severity = sev,
            Title = title,
            NodeLabel = node.Label,
            Details = details,
            Suggestion = suggestion,
            SuggestedSql = sql,
        };
    }

    /// <summary>提案列 <paramref name="cols"/> が、既存インデックスの先頭列群と順に一致する（接頭辞）か。</summary>
    private static bool IsLeadingPrefix(IReadOnlyList<string> indexColumns, IReadOnlyList<string> cols)
    {
        if (cols.Count == 0 || indexColumns.Count < cols.Count) return false;
        for (int i = 0; i < cols.Count; i++)
            if (!string.Equals(indexColumns[i], cols[i], StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    private static string FormatNDistinct(double nd)
        => nd < 0 ? $"{nd:N2}（行数に対する比率）" : $"{nd:N0}";

    private static bool TryGetEqualityLiteral(string filter, string col, out string literal)
    {
        literal = "";
        // col = 'x' / col = 123 の右辺リテラルを取得（::type のキャストは含めない）
        var m = Regex.Match(filter, Regex.Escape(col) + @"\s*=\s*('(?:[^']|'')*'|-?\d+(?:\.\d+)?)",
            RegexOptions.IgnoreCase);
        if (!m.Success) return false;
        literal = m.Groups[1].Value;
        return true;
    }
}
