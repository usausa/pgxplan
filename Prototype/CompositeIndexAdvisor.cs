using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PgPlanCheck;

/// <summary>
/// B2: 複合インデックスの列順を最適化する。キー順は「等値 → 範囲 → ORDER BY」とし、
/// 出力に必要なだけの列は INCLUDE 候補として分離する（Index-Only 化のため）。
/// </summary>
public static class CompositeIndexAdvisor
{
    private static readonly Regex SimpleIdent = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>キー列（順序つき）と INCLUDE 候補列を組み立てる。</summary>
    public static (List<string> Key, List<string> Include) BuildColumns(
        string? filter,
        IReadOnlyList<string>? orderByKeys,
        IReadOnlyList<string>? outputColumns)
    {
        var pred = FilterColumnExtractor.ClassifyPredicates(filter);
        var key = new List<string>();

        void Add(string c)
        {
            if (!key.Any(k => string.Equals(k, c, StringComparison.OrdinalIgnoreCase))) key.Add(c);
        }

        foreach (var c in pred.Equality) Add(c);
        foreach (var c in pred.Range) Add(c);
        if (orderByKeys is not null)
            foreach (var raw in orderByKeys)
                if (Bare(raw) is { } c) Add(c);

        var include = new List<string>();
        if (outputColumns is not null)
            foreach (var raw in outputColumns)
                if (Bare(raw) is { } c
                    && !key.Any(k => string.Equals(k, c, StringComparison.OrdinalIgnoreCase))
                    && !include.Contains(c))
                    include.Add(c);

        return (key, include);
    }

    /// <summary>"orders.created_at DESC" → "created_at"。式や複雑な指定は null。</summary>
    private static string? Bare(string raw)
    {
        var s = raw.Trim();
        int sp = s.IndexOf(' ');           // 並び順 / NULLS 指定を落とす
        if (sp >= 0) s = s.Substring(0, sp);
        int dot = s.LastIndexOf('.');        // 別名/スキーマ修飾を落とす
        if (dot >= 0) s = s.Substring(dot + 1);
        return SimpleIdent.IsMatch(s) ? s : null;
    }
}
