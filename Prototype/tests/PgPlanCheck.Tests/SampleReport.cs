using System.Collections.Generic;
using PgPlanCheck;
using PgPlanCheck.Reporting;

namespace PgPlanCheck.Tests;

/// <summary>構造化出力・CI ゲートのテスト用に、DB 非依存で組み立てる解析結果。</summary>
internal static class SampleReport
{
    /// <summary>警告 1 / 情報 1 / 検証エラー 1 / スキップ 1 を含むレポート。</summary>
    public static AnalysisReport Mixed()
    {
        var report = new AnalysisReport { Metadata = new RunMetadata { Analyzed = true } };

        var src = new SourceResult { Name = "samples/01.sql" };
        report.Sources.Add(src);
        src.Statements.Add(new StatementResult
        {
            Index = 1,
            Sql = "SELECT * FROM orders WHERE status = 'pending'",
            PlanningTimeMs = 0.2,
            ExecutionTimeMs = 60,
            Findings = new List<Finding>
            {
                new()
                {
                    Severity = Severity.Warning,
                    Title = "シーケンシャルスキャン（インデックス未使用の全件走査）",
                    NodeLabel = "Seq Scan on explain_demo.orders",
                    Details = new List<string> { "フィルタ: status = 'pending'" },
                    Suggestion = "インデックスを作成してください。",
                    SuggestedSql = "CREATE INDEX idx_orders_status ON explain_demo.orders (status);",
                    RuleId = "seq-scan",
                },
                new()
                {
                    Severity = Severity.Info,
                    Title = "最も時間を要したノード",
                    NodeLabel = "Sort",
                    Details = new List<string>(),
                    RuleId = "bottleneck",
                },
            },
        });
        src.Statements.Add(new StatementResult { Index = 2, Sql = "UPDATE orders SET x=1", Skipped = true, SkipKeyword = "update" });

        var bad = new SourceResult { Name = "samples/bad.sql" };
        report.Sources.Add(bad);
        bad.Statements.Add(new StatementResult { Index = 1, Sql = "SELECT zzz FROM orders", Error = "42703 column \"zzz\" does not exist" });

        return report;
    }

    /// <summary>情報のみ（警告・重大・エラーなし）のレポート。</summary>
    public static AnalysisReport InfoOnly()
    {
        var report = new AnalysisReport { Metadata = new RunMetadata { Analyzed = true } };
        var src = new SourceResult { Name = "x.sql" };
        report.Sources.Add(src);
        src.Statements.Add(new StatementResult
        {
            Index = 1,
            Sql = "SELECT 1",
            Findings = new List<Finding>
            {
                new() { Severity = Severity.Info, Title = "情報", NodeLabel = "Result", RuleId = "bottleneck" },
            },
        });
        return report;
    }
}
