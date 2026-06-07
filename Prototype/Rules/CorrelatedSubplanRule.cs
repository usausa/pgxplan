using System.Collections.Generic;

namespace PgPlanCheck.Rules;

/// <summary>A4: 相関サブプラン（SubPlan）が外側の行ごとに多数回実行されているのを検出する。</summary>
public sealed class CorrelatedSubplanRule : IPlanRule
{
    public string Id => "correlated-subplan";

    private const double LoopThreshold = 1_000;

    public IEnumerable<Finding> Analyze(PlanContext ctx)
    {
        if (!ctx.Analyzed) yield break;

        foreach (var nr in ctx.Nodes)
        {
            var node = nr.Node;
            if (node.ParentRelationship != "SubPlan") continue;
            double loops = node.Loops;
            if (loops < LoopThreshold) continue;

            var details = new List<string>
            {
                $"サブプラン: {node.Label}",
                $"実行回数(loops): {loops:N0}",
            };
            if (node.ActualTotalTime is { } t) details.Add($"1 回あたり: {t:N3} ms（合計 ≒ {t * loops:N1} ms）");

            yield return new Finding
            {
                Severity = Severity.Warning,
                Title = "相関サブプランの多数回実行",
                NodeLabel = node.Label,
                Details = details,
                Suggestion = "外側の行ごとに相関サブクエリ（SubPlan）が繰り返し実行されています。"
                           + "JOIN や LATERAL、集約への書き換えで 1 回の走査にまとめられないか検討してください。",
                SuggestedSql = null,
            };
        }
    }
}
