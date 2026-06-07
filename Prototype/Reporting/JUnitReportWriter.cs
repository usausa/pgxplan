using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace PgPlanCheck.Reporting;

/// <summary>
/// 解析結果を JUnit XML で出力する（D1）。ソース=testsuite、文=testcase。
/// 警告/重大の指摘があれば failure、検証エラーは error、情報のみ・問題なしは pass とする。
/// </summary>
public sealed class JUnitReportWriter : IReportWriter
{
    public void Write(AnalysisReport r, TextWriter output)
    {
        var suites = new XElement("testsuites", new XAttribute("name", r.Metadata.Tool));
        int gTests = 0, gFail = 0, gErr = 0;

        foreach (var s in r.Sources)
        {
            var suite = new XElement("testsuite", new XAttribute("name", s.Name));
            int tests = 0, fails = 0, errs = 0;

            foreach (var st in s.Statements)
            {
                tests++;
                var tc = new XElement("testcase",
                    new XAttribute("name", $"statement #{st.Index}"),
                    new XAttribute("classname", s.Name));

                if (st.Skipped)
                {
                    tc.Add(new XElement("skipped", new XAttribute("message", $"EXPLAIN 対象外: {st.SkipKeyword}")));
                }
                else if (st.Error is not null)
                {
                    tc.Add(new XElement("error", new XAttribute("message", FirstLine(st.Error)), st.Error));
                    errs++;
                }
                else
                {
                    var blocking = st.Findings.Where(f => f.Severity != Severity.Info).ToList();
                    if (blocking.Count > 0)
                    {
                        tc.Add(new XElement("failure",
                            new XAttribute("message", $"{blocking.Count} 件の指摘（警告/重大）"),
                            FindingsText(st.Findings)));
                        fails++;
                    }
                    else if (st.Findings.Count > 0)
                    {
                        tc.Add(new XElement("system-out", FindingsText(st.Findings)));
                    }
                }

                suite.Add(tc);
            }

            suite.Add(new XAttribute("tests", tests), new XAttribute("failures", fails), new XAttribute("errors", errs));
            suites.Add(suite);
            gTests += tests; gFail += fails; gErr += errs;
        }

        suites.Add(new XAttribute("tests", gTests), new XAttribute("failures", gFail), new XAttribute("errors", gErr));

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), suites);
        doc.Save(output);
        output.WriteLine();
    }

    private static string FirstLine(string s)
    {
        var i = s.IndexOf('\n');
        return i < 0 ? s : s.Substring(0, i);
    }

    private static string FindingsText(System.Collections.Generic.IEnumerable<Finding> findings)
    {
        var sb = new StringBuilder();
        foreach (var f in findings)
        {
            sb.AppendLine($"[{SeverityText.Ja(f.Severity)}] {f.Title} @ {f.NodeLabel}");
            foreach (var d in f.Details) sb.AppendLine($"  - {d}");
            if (f.Suggestion is not null) sb.AppendLine($"  → {f.Suggestion}");
            if (f.SuggestedSql is not null) sb.AppendLine($"  {f.SuggestedSql}");
        }
        return sb.ToString().TrimEnd();
    }
}
