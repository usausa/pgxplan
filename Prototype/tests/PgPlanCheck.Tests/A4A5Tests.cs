using PgPlanCheck;
using Xunit;

namespace PgPlanCheck.Tests;

/// <summary>A4（追加ルール群）と A5（推定乖離の高度化）のテスト。</summary>
public class A4A5Tests
{
    private const string CorrelatedSubplan = """
    [ { "Plan": {
        "Node Type": "Seq Scan", "Relation Name": "orders", "Schema": "explain_demo",
        "Total Cost": 100, "Plan Rows": 1000, "Plan Width": 40,
        "Actual Total Time": 50, "Actual Rows": 1000, "Actual Loops": 1,
        "Plans": [ {
          "Node Type": "Aggregate", "Parent Relationship": "SubPlan", "Subplan Name": "SubPlan 1",
          "Total Cost": 5, "Plan Rows": 1, "Plan Width": 8,
          "Actual Total Time": 0.01, "Actual Rows": 1, "Actual Loops": 5000
        } ]
      } } ]
    """;

    private const string IndexOnlyHeap = """
    [ { "Plan": {
        "Node Type": "Index Only Scan", "Index Name": "orders_pkey", "Relation Name": "orders", "Schema": "explain_demo",
        "Heap Fetches": 5000, "Total Cost": 100, "Plan Rows": 5000, "Plan Width": 8,
        "Actual Total Time": 10, "Actual Rows": 5000, "Actual Loops": 1
      } } ]
    """;

    private const string ParallelShortfall = """
    [ { "Plan": {
        "Node Type": "Gather", "Workers Planned": 4, "Workers Launched": 1,
        "Total Cost": 100, "Plan Rows": 1000, "Plan Width": 40,
        "Actual Total Time": 50, "Actual Rows": 1000, "Actual Loops": 1,
        "Plans": [ {
          "Node Type": "Seq Scan", "Parent Relationship": "Outer", "Relation Name": "orders", "Schema": "explain_demo",
          "Total Cost": 50, "Plan Rows": 1000, "Plan Width": 40,
          "Actual Total Time": 40, "Actual Rows": 250, "Actual Loops": 1
        } ]
      } } ]
    """;

    private const string JitHeavy = """
    [ { "Plan": {
        "Node Type": "Result", "Total Cost": 1, "Plan Rows": 1, "Plan Width": 4,
        "Actual Total Time": 1, "Actual Rows": 1, "Actual Loops": 1
      }, "Execution Time": 100, "JIT": { "Functions": 10, "Timing": { "Total": 50 } } } ]
    """;

    private const string TriggerHeavy = """
    [ { "Plan": {
        "Node Type": "Insert", "Relation Name": "orders", "Schema": "explain_demo",
        "Total Cost": 1, "Plan Rows": 1, "Plan Width": 4,
        "Actual Total Time": 1, "Actual Rows": 0, "Actual Loops": 1
      }, "Execution Time": 100, "Triggers": [ { "Trigger Name": "trg_audit", "Relation": "orders", "Time": 50, "Calls": 1000 } ] } ]
    """;

    private const string CartesianJoin = """
    [ { "Plan": {
        "Node Type": "Nested Loop", "Join Type": "Inner",
        "Total Cost": 100000, "Plan Rows": 100000, "Plan Width": 40,
        "Plans": [
          { "Node Type": "Seq Scan", "Parent Relationship": "Outer", "Relation Name": "a", "Schema": "s", "Total Cost": 10, "Plan Rows": 1000, "Plan Width": 4 },
          { "Node Type": "Seq Scan", "Parent Relationship": "Inner", "Relation Name": "b", "Schema": "s", "Total Cost": 10, "Plan Rows": 100, "Plan Width": 4 }
        ]
      } } ]
    """;

    private const string MultiColUnderEstimate = """
    [ { "Plan": {
        "Node Type": "Seq Scan", "Relation Name": "orders", "Schema": "explain_demo",
        "Filter": "((status = 'pending'::text) AND (region = 'north'::text))",
        "Total Cost": 100, "Plan Rows": 10, "Plan Width": 40,
        "Actual Total Time": 5, "Actual Rows": 5000, "Actual Loops": 1, "Rows Removed by Filter": 0
      } } ]
    """;

    private const string LimitOverEstimate = """
    [ { "Plan": {
        "Node Type": "Limit", "Total Cost": 5, "Plan Rows": 50, "Plan Width": 40,
        "Actual Total Time": 1, "Actual Rows": 50, "Actual Loops": 1,
        "Plans": [ {
          "Node Type": "Seq Scan", "Relation Name": "orders", "Schema": "explain_demo",
          "Total Cost": 100, "Plan Rows": 100000, "Plan Width": 40,
          "Actual Total Time": 1, "Actual Rows": 50, "Actual Loops": 1
        } ]
      } } ]
    """;

    [Fact]
    public void Correlated_subplan_detected()
        => Assert.Contains(TestData.Analyze(CorrelatedSubplan, true), f => f.RuleId == "correlated-subplan");

    [Fact]
    public void Index_only_heap_fetches_suggest_vacuum()
    {
        var f = Assert.Single(TestData.Analyze(IndexOnlyHeap, true), x => x.RuleId == "index-only-heap-fetch");
        Assert.Contains("VACUUM", f.SuggestedSql!);
    }

    [Fact]
    public void Parallel_worker_shortfall_detected()
        => Assert.Contains(TestData.Analyze(ParallelShortfall, true), f => f.RuleId == "parallel-workers");

    [Fact]
    public void Jit_overhead_detected()
    {
        var f = Assert.Single(TestData.Analyze(JitHeavy, true), x => x.RuleId == "jit-overhead");
        Assert.Equal(Severity.Warning, f.Severity);
    }

    [Fact]
    public void Trigger_time_detected()
        => Assert.Contains(TestData.Analyze(TriggerHeavy, true), f => f.RuleId == "trigger-time");

    [Fact]
    public void Cartesian_product_detected_in_verify_mode()
        => Assert.Contains(TestData.Analyze(CartesianJoin, false), f => f.RuleId == "cartesian-join");

    [Fact]
    public void Multicolumn_under_estimate_suggests_create_statistics()
    {
        var f = Assert.Single(TestData.Analyze(MultiColUnderEstimate, true), x => x.RuleId == "estimation-error");
        Assert.Contains("CREATE STATISTICS", f.SuggestedSql!);
        Assert.Contains("status, region", f.SuggestedSql!);
    }

    [Fact]
    public void Over_estimate_under_limit_is_not_flagged()
        => Assert.DoesNotContain(TestData.Analyze(LimitOverEstimate, true), f => f.RuleId == "estimation-error");
}
