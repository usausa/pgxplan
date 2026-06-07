using System;
using System.Collections.Generic;
using PgPlanCheck;
using PgPlanCheck.Catalog;
using Xunit;

namespace PgPlanCheck.Tests;

/// <summary>B1: カタログ参照（既存インデックス・列統計）のテスト。</summary>
public class CatalogTests
{
    private static CatalogSnapshot WithIndex(params string[] columns) => new(
        new[] { new IndexInfo("explain_demo", "orders", "orders_status_idx", columns, false, false) },
        Array.Empty<ColumnStats>());

    [Fact]
    public void Snapshot_returns_indexes_by_table()
    {
        var snap = WithIndex("status");

        var ix = snap.GetIndexes("explain_demo", "orders");
        Assert.Single(ix);
        Assert.Equal("orders_status_idx", ix[0].Name);

        Assert.Empty(snap.GetIndexes(null, "customers"));
    }

    [Fact]
    public void Existing_index_suppresses_create_index_suggestion()
    {
        var findings = TestData.Analyze(TestData.SeqScanSelective, analyzed: true, catalog: WithIndex("status"));

        var seq = Assert.Single(findings, f => f.RuleId == "seq-scan");
        Assert.Null(seq.SuggestedSql);
        Assert.Contains("既に存在", seq.Suggestion!);
        Assert.Contains("orders_status_idx", seq.Suggestion!);
    }

    [Fact]
    public void Composite_index_leading_with_column_also_suppresses_suggestion()
    {
        // (status, created_at) のような複合索引でも、先頭が status なら重複提案を避ける。
        var findings = TestData.Analyze(TestData.SeqScanSelective, analyzed: true, catalog: WithIndex("status", "created_at"));

        var seq = Assert.Single(findings, f => f.RuleId == "seq-scan");
        Assert.Null(seq.SuggestedSql);
    }

    [Fact]
    public void Low_cardinality_stats_add_selectivity_note_and_partial_index_hint()
    {
        var snap = new CatalogSnapshot(
            Array.Empty<IndexInfo>(),
            new[] { new ColumnStats("explain_demo", "orders", "status", NDistinct: 5, Correlation: 0.1, NullFraction: 0) });

        var findings = TestData.Analyze(TestData.SeqScanSelective, analyzed: true, catalog: snap);

        var seq = Assert.Single(findings, f => f.RuleId == "seq-scan");
        Assert.Contains(seq.Details, d => d.Contains("n_distinct=5"));
        Assert.Contains("CREATE INDEX", seq.SuggestedSql!);
        Assert.Contains("部分インデックス", seq.SuggestedSql!);
        Assert.Contains("status = 'pending'", seq.SuggestedSql!);
    }
}
