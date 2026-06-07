using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using PgPlanCheck.Reporting;
using Xunit;

namespace PgPlanCheck.Tests;

/// <summary>D1: 構造化出力（JSON / SARIF / JUnit / Markdown）と GitHub アノテーションのテスト。</summary>
public class ReportWriterTests
{
    private static string Render(IReportWriter w)
    {
        var sw = new StringWriter();
        w.Write(SampleReport.Mixed(), sw);
        return sw.ToString();
    }

    [Fact]
    public void Json_has_summary_and_fingerprints()
    {
        using var doc = JsonDocument.Parse(Render(new JsonReportWriter()));
        var root = doc.RootElement;

        Assert.Equal("PgPlanCheck", root.GetProperty("tool").GetString());
        var summary = root.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("warning").GetInt32());
        Assert.Equal(1, summary.GetProperty("info").GetInt32());
        Assert.Equal(1, summary.GetProperty("errors").GetInt32());

        var first = root.GetProperty("sources")[0].GetProperty("statements")[0].GetProperty("findings")[0];
        Assert.Equal("seq-scan", first.GetProperty("ruleId").GetString());
        Assert.Equal("Warning", first.GetProperty("severity").GetString());
        Assert.False(string.IsNullOrEmpty(first.GetProperty("fingerprint").GetString()));
    }

    [Fact]
    public void Sarif_is_version_2_1_0_with_results()
    {
        using var doc = JsonDocument.Parse(Render(new SarifReportWriter()));
        var root = doc.RootElement;

        Assert.Equal("2.1.0", root.GetProperty("version").GetString());
        var results = root.GetProperty("runs")[0].GetProperty("results");
        Assert.Equal(3, results.GetArrayLength()); // 指摘 2 + 検証エラー 1
        Assert.Contains(results.EnumerateArray(), x => x.GetProperty("ruleId").GetString() == "explain-error");
        Assert.Contains(results.EnumerateArray(), x => x.GetProperty("level").GetString() == "warning");
    }

    [Fact]
    public void Junit_counts_failures_and_errors()
    {
        var doc = XDocument.Parse(Render(new JUnitReportWriter()));
        var suites = doc.Root!;

        Assert.Equal("testsuites", suites.Name.LocalName);
        Assert.Equal("3", suites.Attribute("tests")!.Value);     // 文 3 つ
        Assert.Equal("1", suites.Attribute("failures")!.Value);  // 警告のある文
        Assert.Equal("1", suites.Attribute("errors")!.Value);    // 検証エラーの文
    }

    [Fact]
    public void Markdown_contains_title_and_findings()
    {
        var md = Render(new MarkdownReportWriter());
        Assert.Contains("# PgPlanCheck レポート", md);
        Assert.Contains("シーケンシャルスキャン", md);
        Assert.Contains("検証エラー", md);
        Assert.Contains("```sql", md);
    }

    [Fact]
    public void GitHub_annotations_emit_commands_and_escape_newlines()
    {
        var sw = new StringWriter();
        GitHubAnnotations.Emit(SampleReport.Mixed(), sw);
        var s = sw.ToString();

        Assert.Contains("::warning ", s);
        Assert.Contains("::notice ", s);
        Assert.Contains("::error ", s);
        Assert.Contains("%0A", s); // 改行はエスケープされる
    }
}
