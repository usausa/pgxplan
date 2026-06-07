using System.IO;
using System.Threading.Tasks;
using PgPlanCheck;
using PgPlanCheck.Reporting;
using Xunit;

namespace PgPlanCheck.Tests;

/// <summary>D2: フィンガープリントとベースライン（既知問題の除外）のテスト。</summary>
public class FingerprintBaselineTests
{
    [Fact]
    public void Fingerprint_is_path_independent_and_stable()
    {
        var f = new Finding
        {
            RuleId = "seq-scan",
            NodeLabel = "Seq Scan on explain_demo.orders",
            Title = "シーケンシャルスキャン",
            Severity = Severity.Warning,
        };

        var a = Fingerprint.Of("samples/01.sql", f);
        var b = Fingerprint.Of("/abs/other/dir/01.sql", f); // 同じファイル名 → 同一指紋

        Assert.Equal(a, b);
        Assert.Contains("seq-scan", a);
        Assert.Contains("01.sql", a);
    }

    [Fact]
    public async Task Baseline_roundtrips_through_file()
    {
        var report = SampleReport.Mixed();
        var path = Path.Combine(Path.GetTempPath(), $"pgpc_baseline_{System.Guid.NewGuid():N}.json");
        try
        {
            await Baseline.WriteAsync(path, report);
            var set = await Baseline.LoadAsync(path);

            Assert.NotEmpty(set);
            var fp = Fingerprint.Of(report.Sources[0].Name, report.Sources[0].Statements[0].Findings[0]);
            Assert.Contains(fp, set);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
