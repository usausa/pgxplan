using System;
using System.Collections.Generic;

namespace PgPlanCheck.Rules;

/// <summary>最も自己時間（子を除く処理時間）を要したノードを「集中点」として提示する（実測モードのみ）。</summary>
public sealed class BottleneckRule : IPlanRule
{
    public string Id => "bottleneck";

    public IEnumerable<Finding> Analyze(PlanContext ctx)
    {
        if (!ctx.Analyzed) yield break;
        if (ctx.Nodes.Count <= 1) yield break;

        // 各ノードの自己時間（子を除く・ループ/ワーカー込み）。並列ノードは合算が実時間を超えるため、
        // 実行時間ではなく「全ノードの自己時間合計」に対する割合で示す（並列分も含む処理量の割合）。
        PlanNode? worst = null;
        double worstSelf = -1, totalSelf = 0;
        bool worstParallel = false;
        foreach (var nr in ctx.Nodes)
        {
            double childTime = 0;
            if (nr.Node.Plans is not null)
                foreach (var ch in nr.Node.Plans) childTime += ch.InclusiveTimeMs;
            double self = Math.Max(0, nr.Node.InclusiveTimeMs - childTime);
            totalSelf += self;
            if (self > worstSelf) { worstSelf = self; worst = nr.Node; worstParallel = nr.Node.Loops > 1; }
        }
        if (worst is null || worstSelf <= 0 || totalSelf <= 0) yield break;

        double pct = worstSelf / totalSelf;
        var detail = $"処理時間の {pct:P0} がこのノードに集中（自己時間 概算 {worstSelf:N1} ms）";
        if (worstParallel) detail += "（並列ワーカー込みの累計）";

        yield return new Finding
        {
            Severity = Severity.Info,
            Title = "最も時間を要したノード（処理時間の集中点）",
            NodeLabel = worst.Label,
            Details = new List<string> { detail },
            Suggestion = "まずはこのノードの改善が最も効果的です。上の他の警告と併せて確認してください。",
            SuggestedSql = null,
        };
    }
}
