using PgPlanCheck.Reporting;
using Xunit;

namespace PgPlanCheck.Tests;

/// <summary>D2: CI ゲート（fail-on）と形式パースのテスト。</summary>
public class CiGateTests
{
    [Theory]
    [InlineData(FailOnLevel.None, false)]
    [InlineData(FailOnLevel.Info, true)]
    [InlineData(FailOnLevel.Warning, true)]
    [InlineData(FailOnLevel.Critical, true)] // 検証エラーがあるため critical でも失敗
    public void Mixed_report_fail_on(FailOnLevel level, bool expectedFail)
    {
        Assert.Equal(expectedFail, CiGate.Fails(SampleReport.Mixed(), level));
    }

    [Theory]
    [InlineData(FailOnLevel.None, false)]
    [InlineData(FailOnLevel.Info, true)]
    [InlineData(FailOnLevel.Warning, false)]
    [InlineData(FailOnLevel.Critical, false)]
    public void Info_only_report_fail_on(FailOnLevel level, bool expectedFail)
    {
        Assert.Equal(expectedFail, CiGate.Fails(SampleReport.InfoOnly(), level));
    }

    [Fact]
    public void Format_parsing()
    {
        Assert.True(FormatParsing.TryParseFormat("sarif", out var f));
        Assert.Equal(OutputFormat.Sarif, f);
        Assert.True(FormatParsing.TryParseFormat("md", out var f2));
        Assert.Equal(OutputFormat.Markdown, f2);
        Assert.False(FormatParsing.TryParseFormat("bogus", out _));
    }

    [Fact]
    public void FailOn_parsing()
    {
        Assert.True(FormatParsing.TryParseFailOn("critical", out var l));
        Assert.Equal(FailOnLevel.Critical, l);
        Assert.True(FormatParsing.TryParseFailOn("none", out var l2));
        Assert.Equal(FailOnLevel.None, l2);
        Assert.False(FormatParsing.TryParseFailOn("bogus", out _));
    }
}
