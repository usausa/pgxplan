using System.IO;
using System.Linq;
using System.Text;

namespace PgPlanCheck.Reporting;

/// <summary>解析結果を Markdown で出力する（D1。PR コメント等での共有向け）。</summary>
public sealed class MarkdownReportWriter : IReportWriter
{
    public void Write(AnalysisReport r, TextWriter output)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# PgPlanCheck レポート");
        sb.AppendLine();
        sb.AppendLine($"- ツール: {r.Metadata.Tool} {r.Metadata.Version}");
        sb.AppendLine($"- モード: {(r.Metadata.Analyzed ? "実測 (--analyze)" : "検証専用")}");
        sb.AppendLine($"- 日時: {r.Metadata.Timestamp:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("| 重大 | 警告 | 情報 | 検証エラー | 抑止 | 文数 |");
        sb.AppendLine("|---:|---:|---:|---:|---:|---:|");
        sb.AppendLine($"| {r.Critical} | {r.Warning} | {r.Info} | {r.Errors} | {r.SuppressedCount} | {r.StatementCount} |");
        sb.AppendLine();

        foreach (var s in r.Sources)
        {
            sb.AppendLine($"## {s.Name}");
            sb.AppendLine();
            foreach (var st in s.Statements)
            {
                if (st.Skipped)
                {
                    sb.AppendLine($"### #{st.Index} — スキップ（{st.SkipKeyword}）");
                    sb.AppendLine();
                    continue;
                }
                if (st.Error is not null)
                {
                    sb.AppendLine($"### #{st.Index} — ❌ 検証エラー");
                    sb.AppendLine();
                    sb.AppendLine("```");
                    sb.AppendLine(st.Error);
                    sb.AppendLine("```");
                    sb.AppendLine();
                    continue;
                }

                var head = st.Findings.Count == 0 ? "✅ 問題なし" : $"{st.Findings.Count} 件の指摘";
                sb.AppendLine($"### #{st.Index} — {head}");
                sb.AppendLine();
                foreach (var f in st.Findings)
                {
                    sb.AppendLine($"- **[{SeverityText.Ja(f.Severity)}] {f.Title}** — `{f.NodeLabel}`");
                    foreach (var d in f.Details) sb.AppendLine($"  - {d}");
                    if (f.Suggestion is not null) sb.AppendLine($"  - 改善案: {f.Suggestion}");
                    if (f.SuggestedSql is not null)
                    {
                        sb.AppendLine();
                        sb.AppendLine("  ```sql");
                        foreach (var line in f.SuggestedSql.Replace("\r\n", "\n").Split('\n'))
                            sb.AppendLine($"  {line}");
                        sb.AppendLine("  ```");
                    }
                }
                sb.AppendLine();
            }
        }

        var recs = WorkloadAdvisor.Recommend(r);
        if (recs.Count > 0)
        {
            sb.AppendLine("## 推奨インデックス（ワークロード統合）");
            sb.AppendLine();
            foreach (var rec in recs)
            {
                sb.AppendLine($"- **{rec.Occurrences} 文で有効** — 対象: {string.Join(", ", rec.Sources)}");
                sb.AppendLine("  ```sql");
                sb.AppendLine($"  {rec.CreateSql}");
                sb.AppendLine("  ```");
            }
            sb.AppendLine();
        }

        output.Write(sb.ToString());
    }
}
