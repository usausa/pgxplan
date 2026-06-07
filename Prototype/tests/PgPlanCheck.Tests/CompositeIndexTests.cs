using PgPlanCheck;
using Xunit;

namespace PgPlanCheck.Tests;

/// <summary>B2: 述語分類と複合インデックスの列順最適化のテスト。</summary>
public class CompositeIndexTests
{
    [Fact]
    public void Classify_separates_equality_and_range()
    {
        var pred = FilterColumnExtractor.ClassifyPredicates("((status = 'x'::text) AND (amount > 100))");
        Assert.Equal(new[] { "status" }, pred.Equality);
        Assert.Equal(new[] { "amount" }, pred.Range);
    }

    [Fact]
    public void Build_columns_orders_equality_then_range_then_orderby()
    {
        var (key, include) = CompositeIndexAdvisor.BuildColumns(
            "((status = 'x'::text) AND (amount > 100))",
            orderByKeys: new[] { "orders.created_at DESC" },
            outputColumns: new[] { "id", "note", "status" });

        Assert.Equal(new[] { "status", "amount", "created_at" }, key);
        Assert.Contains("id", include);
        Assert.Contains("note", include);
        Assert.DoesNotContain("status", include); // キーに含まれる列は INCLUDE に出さない
    }

    [Fact]
    public void Build_columns_handles_empty_filter()
    {
        var (key, include) = CompositeIndexAdvisor.BuildColumns(null, null, null);
        Assert.Empty(key);
        Assert.Empty(include);
    }
}
