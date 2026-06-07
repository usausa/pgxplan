using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace PgPlanCheck.Reporting;

/// <summary>B4: ワークロード統合インデックス推奨を、適用可能な SQL スクリプトとして書き出す。</summary>
public static class AdviceScript
{
    public static async Task WriteAsync(string path, IReadOnlyList<IndexRecommendation> recs, AnalysisReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"-- PgPlanCheck 推奨インデックス（ワークロード統合, {recs.Count} 件）  {report.Metadata.Timestamp:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("-- 効果（出現回数）順。適用前に必ずレビューしてください。");
        sb.AppendLine();
        foreach (var r in recs)
        {
            sb.AppendLine($"-- {r.Occurrences} 文で推奨 / 対象: {string.Join(", ", r.Sources)}");
            sb.AppendLine(r.CreateSql);
            sb.AppendLine();
        }
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(false));
    }
}
