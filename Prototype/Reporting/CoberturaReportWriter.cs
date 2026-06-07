using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PgPlanCheck.Reporting;

/// <summary>
/// Cobertura カバレッジ XML として出力する（D1）。各 SQL 文を 1 行に見立て、
/// 問題のない文（警告/重大/検証エラーが無い）を「カバー済み(hits=1)」、それ以外を hits=0 とする。
/// CI（GitLab/Jenkins 等）で「SQL 健全度」をカバレッジとして可視化できる。
/// </summary>
public sealed class CoberturaReportWriter : IReportWriter
{
    public void Write(AnalysisReport r, TextWriter output)
    {
        int totalValid = 0, totalCovered = 0;
        var classes = new XElement("classes");

        foreach (var s in r.Sources)
        {
            var lines = new XElement("lines");
            int valid = 0, covered = 0;
            foreach (var st in s.Statements)
            {
                int hits = IsClean(st) ? 1 : 0;
                lines.Add(new XElement("line", new XAttribute("number", st.Index), new XAttribute("hits", hits)));
                valid++; covered += hits;
            }
            totalValid += valid; totalCovered += covered;

            classes.Add(new XElement("class",
                new XAttribute("name", s.Name),
                new XAttribute("filename", s.Name),
                new XAttribute("line-rate", Rate(valid > 0 ? (double)covered / valid : 1d)),
                new XAttribute("branch-rate", "0"),
                new XAttribute("complexity", "0"),
                new XElement("methods"),
                lines));
        }

        string lineRate = Rate(totalValid > 0 ? (double)totalCovered / totalValid : 1d);
        var coverage = new XElement("coverage",
            new XAttribute("line-rate", lineRate),
            new XAttribute("branch-rate", "0"),
            new XAttribute("lines-covered", totalCovered),
            new XAttribute("lines-valid", totalValid),
            new XAttribute("branches-covered", 0),
            new XAttribute("branches-valid", 0),
            new XAttribute("complexity", 0),
            new XAttribute("version", r.Metadata.Version),
            new XAttribute("timestamp", r.Metadata.Timestamp.ToUnixTimeSeconds()),
            new XElement("sources", new XElement("source", ".")),
            new XElement("packages",
                new XElement("package",
                    new XAttribute("name", r.Metadata.Tool),
                    new XAttribute("line-rate", lineRate),
                    new XAttribute("branch-rate", "0"),
                    new XAttribute("complexity", "0"),
                    classes)));

        // DOCTYPE は付けない（coverlet 等の de-facto 出力に合わせ、外部 DTD 参照による解析問題も避ける）。
        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), coverage);
        doc.Save(output);
        output.WriteLine();
    }

    // 警告・重大・検証エラーが無ければ「健全（カバー済み）」とみなす（情報・スキップは健全扱い）。
    private static bool IsClean(StatementResult st)
        => st.Error is null && !st.Findings.Any(f => f.Severity != Severity.Info);

    private static string Rate(double v) => v.ToString("0.0000", CultureInfo.InvariantCulture);
}
