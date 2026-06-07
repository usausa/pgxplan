using System.Collections.Generic;
using System.Text.Json;
using PgPlanCheck;
using PgPlanCheck.Catalog;

namespace PgPlanCheck.Tests;

/// <summary>
/// 固定の EXPLAIN(JSON) フィクスチャと、解析を呼び出すヘルパー（E1）。
/// 実 DB に接続せず、JSON → モデル → PlanAnalyzer → Finding を検証する。
/// </summary>
internal static class TestData
{
    public static ExplainRoot Parse(string json)
        => JsonSerializer.Deserialize<List<ExplainRoot>>(json)![0];

    public static IReadOnlyList<Finding> Analyze(string json, bool analyzed, ICatalog? catalog = null)
        => new PlanAnalyzer().Analyze(Parse(json), analyzed, catalog);

    // status='pending'（2%）の選択的な全件走査。
    public const string SeqScanSelective = """
    [ { "Plan": {
        "Node Type": "Seq Scan", "Relation Name": "orders", "Schema": "explain_demo", "Alias": "orders",
        "Startup Cost": 0.0, "Total Cost": 9000.0, "Plan Rows": 10000, "Plan Width": 40,
        "Actual Startup Time": 0.1, "Actual Total Time": 50.0, "Actual Rows": 10000, "Actual Loops": 1,
        "Filter": "(status = 'pending'::text)", "Rows Removed by Filter": 490000
      }, "Planning Time": 0.2, "Execution Time": 60.0 } ]
    """;

    // 型不一致キャスト。走査量が小さく Seq Scan ルールは発火しない（A3 のみ）。
    public const string SeqScanCastSmall = """
    [ { "Plan": {
        "Node Type": "Seq Scan", "Relation Name": "orders", "Schema": "explain_demo", "Alias": "o",
        "Total Cost": 10.0, "Plan Rows": 1, "Plan Width": 40,
        "Actual Total Time": 0.1, "Actual Rows": 1, "Actual Loops": 1,
        "Filter": "((customer_id)::text = '123'::text)", "Rows Removed by Filter": 0
      } } ]
    """;

    // 型不一致キャスト＋選択的な全件走査（Seq Scan は提案を A3 に委譲する）。
    public const string SeqScanCastSelective = """
    [ { "Plan": {
        "Node Type": "Seq Scan", "Relation Name": "orders", "Schema": "explain_demo",
        "Total Cost": 9000.0, "Plan Rows": 10000, "Plan Width": 40,
        "Actual Total Time": 50.0, "Actual Rows": 10000, "Actual Loops": 1,
        "Filter": "((customer_id)::text = '123'::text)", "Rows Removed by Filter": 490000
      } } ]
    """;

    public const string FunctionFilterSmall = """
    [ { "Plan": {
        "Node Type": "Seq Scan", "Relation Name": "customers", "Schema": "explain_demo",
        "Total Cost": 10.0, "Plan Rows": 1, "Plan Width": 40,
        "Actual Total Time": 0.1, "Actual Rows": 1, "Actual Loops": 1,
        "Filter": "(lower(name) = 'smith'::text)", "Rows Removed by Filter": 0
      } } ]
    """;

    public const string LeadingWildcardSmall = """
    [ { "Plan": {
        "Node Type": "Seq Scan", "Relation Name": "orders", "Schema": "explain_demo",
        "Total Cost": 10.0, "Plan Rows": 1, "Plan Width": 40,
        "Actual Total Time": 0.1, "Actual Rows": 1, "Actual Loops": 1,
        "Filter": "(note ~~ '%abc%'::text)", "Rows Removed by Filter": 0
      } } ]
    """;

    // note 全件の外部ソート。
    public const string SortExternal = """
    [ { "Plan": {
        "Node Type": "Sort", "Sort Method": "external merge", "Sort Space Type": "Disk",
        "Sort Space Used": 40960, "Sort Key": ["orders.note"],
        "Total Cost": 100.0, "Plan Rows": 500000, "Plan Width": 40,
        "Actual Total Time": 200.0, "Actual Rows": 500000, "Actual Loops": 1,
        "Plans": [ {
          "Node Type": "Seq Scan", "Relation Name": "orders", "Schema": "explain_demo",
          "Total Cost": 50.0, "Plan Rows": 500000, "Plan Width": 40,
          "Actual Total Time": 50.0, "Actual Rows": 500000, "Actual Loops": 1
        } ]
      } } ]
    """;

    public const string HashBatches = """
    [ { "Plan": {
        "Node Type": "Hash", "Hash Batches": 4, "Original Hash Batches": 1, "Peak Memory Usage": 2048,
        "Total Cost": 100.0, "Plan Rows": 50000, "Plan Width": 40,
        "Actual Total Time": 20.0, "Actual Rows": 50000, "Actual Loops": 1
      } } ]
    """;

    // 外部ソートで一時ファイルへ spill（BUFFERS つき）。A2 用。
    public const string TempSpill = """
    [ { "Plan": {
        "Node Type": "Sort", "Sort Method": "external merge", "Sort Space Type": "Disk", "Sort Space Used": 40000,
        "Shared Hit Blocks": 100, "Shared Read Blocks": 50, "Temp Read Blocks": 5000, "Temp Written Blocks": 5000,
        "Total Cost": 100.0, "Plan Rows": 500000, "Plan Width": 40,
        "Actual Total Time": 200.0, "Actual Rows": 500000, "Actual Loops": 1,
        "Plans": [ {
          "Node Type": "Seq Scan", "Relation Name": "orders", "Schema": "explain_demo",
          "Shared Hit Blocks": 80, "Shared Read Blocks": 50, "Temp Read Blocks": 0, "Temp Written Blocks": 0,
          "Total Cost": 50.0, "Plan Rows": 500000, "Plan Width": 40,
          "Actual Total Time": 50.0, "Actual Rows": 500000, "Actual Loops": 1
        } ]
      } } ]
    """;

    // 大量のディスク読み込み（低ヒット率）。A2 用。
    public const string CacheMiss = """
    [ { "Plan": {
        "Node Type": "Seq Scan", "Relation Name": "orders", "Schema": "explain_demo",
        "Shared Hit Blocks": 100, "Shared Read Blocks": 20000,
        "Total Cost": 5000.0, "Plan Rows": 1000000, "Plan Width": 40,
        "Actual Total Time": 300.0, "Actual Rows": 1000000, "Actual Loops": 1
      } } ]
    """;

    // 推定 10 行 / 実測 5000 行（過小推定）。
    public const string EstimationError = """
    [ { "Plan": {
        "Node Type": "Seq Scan", "Relation Name": "orders", "Schema": "explain_demo",
        "Total Cost": 100.0, "Plan Rows": 10, "Plan Width": 40,
        "Actual Total Time": 5.0, "Actual Rows": 5000, "Actual Loops": 1
      } } ]
    """;

    // 主キー検索（正常系）。
    public const string IndexedLookup = """
    [ { "Plan": {
        "Node Type": "Index Scan", "Index Name": "orders_pkey", "Relation Name": "orders", "Schema": "explain_demo",
        "Index Cond": "(id = 12345)",
        "Total Cost": 8.0, "Plan Rows": 1, "Plan Width": 40,
        "Actual Total Time": 0.05, "Actual Rows": 1, "Actual Loops": 1
      } } ]
    """;
}
