using System.Collections.Generic;
using PgPlanCheck;
using PgPlanCheck.Rules;
using Xunit;

namespace PgPlanCheck.Tests;

/// <summary>オーケストレータ（A1）と既存ルールの回帰テスト。</summary>
public class PlanAnalyzerTests
{
    [Fact]
    public void SeqScan_selective_warns_and_suggests_index()
    {
        var findings = TestData.Analyze(TestData.SeqScanSelective, analyzed: true);

        var seq = Assert.Single(findings, f => f.RuleId == "seq-scan");
        Assert.Equal(Severity.Warning, seq.Severity);
        Assert.NotNull(seq.SuggestedSql);
        Assert.Contains("CREATE INDEX", seq.SuggestedSql!);
        Assert.Contains("(status)", seq.SuggestedSql!);
    }

    [Fact]
    public void SeqScan_in_verify_mode_is_info_only()
    {
        var findings = TestData.Analyze(TestData.SeqScanSelective, analyzed: false);

        var seq = Assert.Single(findings, f => f.RuleId == "seq-scan");
        Assert.Equal(Severity.Info, seq.Severity);
    }

    [Fact]
    public void IndexedLookup_produces_no_findings()
    {
        var findings = TestData.Analyze(TestData.IndexedLookup, analyzed: true);
        Assert.Empty(findings);
    }

    [Fact]
    public void External_sort_is_detected()
    {
        var findings = TestData.Analyze(TestData.SortExternal, analyzed: true);
        Assert.Contains(findings, f => f.RuleId == "external-sort" && f.Severity == Severity.Warning);
    }

    [Fact]
    public void Hash_batch_split_is_detected()
    {
        var findings = TestData.Analyze(TestData.HashBatches, analyzed: true);
        Assert.Contains(findings, f => f.RuleId == "hash-batches" && f.Severity == Severity.Warning);
    }

    [Fact]
    public void Estimation_error_is_detected_with_analyze_suggestion()
    {
        var findings = TestData.Analyze(TestData.EstimationError, analyzed: true);

        var est = Assert.Single(findings, f => f.RuleId == "estimation-error");
        Assert.Equal(Severity.Warning, est.Severity); // 500 倍 → Warning
        Assert.Contains("ANALYZE", est.SuggestedSql!);
    }

    [Fact]
    public void Estimation_and_bottleneck_are_skipped_in_verify_mode()
    {
        var findings = TestData.Analyze(TestData.EstimationError, analyzed: false);
        Assert.DoesNotContain(findings, f => f.RuleId == "estimation-error");
        Assert.DoesNotContain(findings, f => f.RuleId == "bottleneck");
    }

    [Fact]
    public void Findings_are_ordered_by_severity_descending()
    {
        var findings = TestData.Analyze(TestData.SeqScanCastSelective, analyzed: true);

        Assert.True(findings.Count >= 2);
        for (int i = 1; i < findings.Count; i++)
            Assert.True((int)findings[i - 1].Severity >= (int)findings[i].Severity);
    }

    [Fact]
    public void Custom_rule_set_runs_only_selected_rules()
    {
        // A1: ルール集合を差し替えられること。SortRule だけなら Seq Scan には何も出ない。
        var analyzer = new PlanAnalyzer(new IPlanRule[] { new SortRule() });
        var findings = analyzer.Analyze(TestData.Parse(TestData.SeqScanSelective), analyzed: true, catalog: null);
        Assert.Empty(findings);
    }

    [Fact]
    public void Every_finding_gets_a_rule_id_stamped()
    {
        var findings = TestData.Analyze(TestData.SortExternal, analyzed: true);
        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.False(string.IsNullOrEmpty(f.RuleId)));
    }
}
