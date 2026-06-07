using PgPlanCheck;
using Xunit;

namespace PgPlanCheck.Tests;

/// <summary>A3: インデックス非効率（sargability）検出のテスト。</summary>
public class SargabilityTests
{
    [Fact]
    public void Detects_column_cast()
    {
        var issues = FilterColumnExtractor.DetectSargability("((customer_id)::text = '123'::text)");
        Assert.Contains(issues, i => i.Kind == SargabilityIssueKind.ColumnCast && i.Column == "customer_id");
    }

    [Fact]
    public void Detects_function_on_column()
    {
        var issues = FilterColumnExtractor.DetectSargability("(lower(name) = 'smith'::text)");
        Assert.Contains(issues, i => i.Kind == SargabilityIssueKind.FunctionOnColumn && i.Column == "name");
    }

    [Fact]
    public void Detects_leading_wildcard()
    {
        var issues = FilterColumnExtractor.DetectSargability("(note ~~ '%abc%'::text)");
        Assert.Contains(issues, i => i.Kind == SargabilityIssueKind.LeadingWildcard && i.Column == "note");
    }

    [Theory]
    [InlineData("(amount > 9500)")]                  // 素の比較
    [InlineData("(status = 'pending'::text)")]        // リテラル側のキャストは問題なし
    [InlineData("(note ~~ 'abc%'::text)")]            // 前方一致はインデックスが効く
    [InlineData("(note !~~ '%abc%'::text)")]          // NOT LIKE は対象外
    public void Sargable_or_irrelevant_conditions_are_not_flagged(string filter)
    {
        Assert.Empty(FilterColumnExtractor.DetectSargability(filter));
    }

    [Fact]
    public void Cast_emits_warning_finding()
    {
        var findings = TestData.Analyze(TestData.SeqScanCastSmall, analyzed: true);

        var s = Assert.Single(findings, f => f.RuleId == "sargability");
        Assert.Equal(Severity.Warning, s.Severity);
        Assert.Contains("型不一致", s.Title);
    }

    [Fact]
    public void Function_finding_suggests_expression_index()
    {
        var findings = TestData.Analyze(TestData.FunctionFilterSmall, analyzed: true);

        var s = Assert.Single(findings, f => f.RuleId == "sargability");
        Assert.Contains("関数", s.Title);
        Assert.Contains("CREATE INDEX", s.SuggestedSql!);
    }

    [Fact]
    public void Leading_wildcard_finding_suggests_trigram_index()
    {
        var findings = TestData.Analyze(TestData.LeadingWildcardSmall, analyzed: true);

        var s = Assert.Single(findings, f => f.RuleId == "sargability");
        Assert.Contains("pg_trgm", s.SuggestedSql!);
    }

    [Fact]
    public void Seqscan_defers_to_sargability_when_condition_is_nonsargable()
    {
        var findings = TestData.Analyze(TestData.SeqScanCastSelective, analyzed: true);

        var seq = Assert.Single(findings, f => f.RuleId == "seq-scan");
        Assert.Null(seq.SuggestedSql); // 単純な索引提案は出さない
        Assert.Contains("インデックス非効率", seq.Suggestion!);
        Assert.Contains(findings, f => f.RuleId == "sargability");
    }

    [Fact]
    public void Sargability_works_in_verify_mode()
    {
        // 構造的判定なので --analyze なしでも検出できる。
        var findings = TestData.Analyze(TestData.SeqScanCastSmall, analyzed: false);
        Assert.Contains(findings, f => f.RuleId == "sargability");
    }
}
