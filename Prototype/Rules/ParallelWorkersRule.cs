using System.Collections.Generic;

namespace PgPlanCheck.Rules;

/// <summary>A4: 計画した並列ワーカーを起動できていない（planned &gt; launched）のを検出する。</summary>
public sealed class ParallelWorkersRule : IPlanRule
{
    public string Id => "parallel-workers";

    public IEnumerable<Finding> Analyze(PlanContext ctx)
    {
        if (!ctx.Analyzed) yield break;

        foreach (var nr in ctx.Nodes)
        {
            var node = nr.Node;
            if (node.WorkersPlanned is not { } planned || planned <= 0) continue;
            int launched = node.WorkersLaunched ?? 0;
            if (launched >= planned) continue;

            yield return new Finding
            {
                Severity = Severity.Info,
                Title = "並列ワーカー不足（planned > launched）",
                NodeLabel = node.Label,
                Details = new List<string> { $"計画 {planned} / 起動 {launched} ワーカー" },
                Suggestion = "計画した並列ワーカー数を起動できていません。max_parallel_workers / "
                           + "max_parallel_workers_per_gather の上限、同時実行による枯渇、テーブルの parallel_workers 設定を確認してください。",
                SuggestedSql = null,
            };
        }
    }
}
