using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PgPlanCheck.Reporting;

/// <summary>1 件の統合インデックス推奨（複数 SQL 横断）。</summary>
public sealed record IndexRecommendation(
    string Table,
    IReadOnlyList<string> Columns,
    string CreateSql,
    int Occurrences,
    IReadOnlyList<string> Sources);

/// <summary>
/// B4: 全 SQL の索引提案（CREATE INDEX）を集約・重複統合し、効果（出現回数）順に並べる。
/// 列が他の推奨の接頭辞になっている場合は上位（より長い）推奨にまとめる。
/// </summary>
public static class WorkloadAdvisor
{
    private static readonly Regex CreateIdx = new(
        @"CREATE\s+INDEX\s+(?:\S+\s+)?ON\s+(\S+)\s*\(([^)]*)\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private sealed class Rec
    {
        public string Table = "";
        public List<string> Columns = new();
        public string CreateSql = "";
        public int Occurrences;
        public HashSet<string> Sources = new(StringComparer.Ordinal);
        public bool Merged;
    }

    public static IReadOnlyList<IndexRecommendation> Recommend(AnalysisReport report)
    {
        var byKey = new Dictionary<string, Rec>(StringComparer.OrdinalIgnoreCase);

        foreach (var (src, _, f) in report.AllFindings())
        {
            if (f.SuggestedSql is null) continue;
            var firstLine = FirstLine(f.SuggestedSql);
            var m = CreateIdx.Match(firstLine);
            if (!m.Success) continue;

            var table = m.Groups[1].Value;
            var cols = m.Groups[2].Value.Split(',')
                .Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
            if (cols.Count == 0) continue;

            var key = table.ToLowerInvariant() + "(" + string.Join(",", cols.Select(c => c.ToLowerInvariant())) + ")";
            if (!byKey.TryGetValue(key, out var rec))
            {
                rec = new Rec { Table = table, Columns = cols, CreateSql = firstLine.TrimEnd(';') + ";" };
                byKey[key] = rec;
            }
            rec.Occurrences++;
            rec.Sources.Add(src.Name);
        }

        var recs = byKey.Values.ToList();
        MergePrefixes(recs);

        return recs
            .Where(r => !r.Merged)
            .OrderByDescending(r => r.Occurrences)
            .ThenBy(r => r.Table, StringComparer.OrdinalIgnoreCase)
            .Select(r => new IndexRecommendation(r.Table, r.Columns, r.CreateSql, r.Occurrences,
                r.Sources.OrderBy(s => s, StringComparer.Ordinal).ToList()))
            .ToList();
    }

    // 同一テーブルで列が他の接頭辞なら、より長い推奨へ畳み込む。
    private static void MergePrefixes(List<Rec> recs)
    {
        foreach (var shorter in recs)
        {
            if (shorter.Merged) continue;
            var super = recs.FirstOrDefault(longer =>
                !ReferenceEquals(longer, shorter) && !longer.Merged &&
                string.Equals(longer.Table, shorter.Table, StringComparison.OrdinalIgnoreCase) &&
                longer.Columns.Count > shorter.Columns.Count &&
                IsPrefix(shorter.Columns, longer.Columns));
            if (super is null) continue;

            super.Occurrences += shorter.Occurrences;
            foreach (var s in shorter.Sources) super.Sources.Add(s);
            shorter.Merged = true;
        }
    }

    private static bool IsPrefix(IReadOnlyList<string> prefix, IReadOnlyList<string> full)
    {
        if (prefix.Count > full.Count) return false;
        for (int i = 0; i < prefix.Count; i++)
            if (!string.Equals(prefix[i], full[i], StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static string FirstLine(string s)
    {
        int i = s.IndexOf('\n');
        return i < 0 ? s : s.Substring(0, i);
    }
}
