using System.Collections.Generic;
using PgPlanCheck;
using PgPlanCheck.Reporting;
using Xunit;

namespace PgPlanCheck.Tests;

/// <summary>B4: ワークロード統合インデックス推奨のテスト。</summary>
public class WorkloadAdvisorTests
{
    private static Finding Idx(string table, string cols, string name) => new()
    {
        Severity = Severity.Warning,
        Title = "シーケンシャルスキャン",
        NodeLabel = $"Seq Scan on {table}",
        RuleId = "seq-scan",
        SuggestedSql = $"CREATE INDEX {name} ON {table} ({cols});",
    };

    private static AnalysisReport BuildReport()
    {
        var report = new AnalysisReport { Metadata = new RunMetadata() };

        var a = new SourceResult { Name = "a.sql" };
        report.Sources.Add(a);
        a.Statements.Add(new StatementResult { Index = 1, Findings = new List<Finding> { Idx("explain_demo.orders", "status", "idx_orders_status") } });
        a.Statements.Add(new StatementResult { Index = 2, Findings = new List<Finding> { Idx("explain_demo.orders", "status, created_at", "idx_orders_status_created") } });

        var b = new SourceResult { Name = "b.sql" };
        report.Sources.Add(b);
        b.Statements.Add(new StatementResult { Index = 1, Findings = new List<Finding> { Idx("explain_demo.orders", "status", "idx_orders_status") } });

        return report;
    }

    [Fact]
    public void Recommendations_merge_prefix_and_count_occurrences()
    {
        var recs = WorkloadAdvisor.Recommend(BuildReport());

        // (status) は (status, created_at) の接頭辞なので統合され、1 件に集約される。
        var rec = Assert.Single(recs);
        Assert.Equal(new[] { "status", "created_at" }, rec.Columns);
        Assert.Equal(3, rec.Occurrences);            // status×2 + 複合×1
        Assert.Equal(new[] { "a.sql", "b.sql" }, rec.Sources);
    }

    [Fact]
    public void Distinct_tables_are_kept_separate()
    {
        var report = new AnalysisReport { Metadata = new RunMetadata() };
        var s = new SourceResult { Name = "q.sql" };
        report.Sources.Add(s);
        s.Statements.Add(new StatementResult
        {
            Index = 1,
            Findings = new List<Finding>
            {
                Idx("explain_demo.orders", "status", "i1"),
                Idx("explain_demo.customers", "region", "i2"),
            },
        });

        var recs = WorkloadAdvisor.Recommend(report);
        Assert.Equal(2, recs.Count);
    }
}
