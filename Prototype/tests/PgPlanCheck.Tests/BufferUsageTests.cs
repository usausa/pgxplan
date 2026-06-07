using PgPlanCheck;
using Xunit;

namespace PgPlanCheck.Tests;

/// <summary>A2: BUFFERS / 一時 I/O 解析のテスト。</summary>
public class BufferUsageTests
{
    [Fact]
    public void Temp_file_spill_is_reported()
    {
        var findings = TestData.Analyze(TestData.TempSpill, analyzed: true);

        var b = Assert.Single(findings, f => f.RuleId == "buffer-usage" && f.Title.Contains("一時ファイル"));
        Assert.Equal(Severity.Warning, b.Severity);
        Assert.Contains(b.Details, d => d.Contains("一時ファイル I/O 合計"));
        Assert.Equal("SET work_mem = '64MB';", b.SuggestedSql);
    }

    [Fact]
    public void Cache_miss_is_reported_as_info()
    {
        var findings = TestData.Analyze(TestData.CacheMiss, analyzed: true);

        var b = Assert.Single(findings, f => f.RuleId == "buffer-usage");
        Assert.Equal(Severity.Info, b.Severity);
        Assert.Contains("キャッシュミス", b.Title);
    }

    [Fact]
    public void No_buffer_findings_in_verify_mode()
    {
        var findings = TestData.Analyze(TestData.TempSpill, analyzed: false);
        Assert.DoesNotContain(findings, f => f.RuleId == "buffer-usage");
    }
}
