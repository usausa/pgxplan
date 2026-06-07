using System.Collections.Generic;
using System.Linq;

namespace PgPlanCheck.Rules;

/// <summary>ネステッドループで内側ノードを多数回実行している（特に内側 Seq Scan）のを検出する。</summary>
public sealed class NestedLoopRule : IPlanRule
{
    public string Id => "nested-loop";

    private const double InnerLoops = 1_000; // 内側をこの回数以上実行したら指摘

    public IEnumerable<Finding> Analyze(PlanContext ctx)
    {
        if (!ctx.Analyzed) yield break;

        foreach (var nr in ctx.Nodes)
        {
            var node = nr.Node;
            if (node.NodeType != "Nested Loop") continue;
            if (node.Plans is not { Count: >= 2 }) continue;

            var inner = node.Plans.FirstOrDefault(p => p.ParentRelationship == "Inner") ?? node.Plans[^1];
            double innerLoops = inner.Loops;
            if (innerLoops < InnerLoops) continue;

            bool innerHasSeqScan = PlanWalker.Walk(inner).Any(x => x.Node.NodeType == "Seq Scan");
            var sev = innerHasSeqScan ? Severity.Critical : Severity.Warning;

            var details = new List<string>
            {
                $"内側ノード: {inner.Label}",
                $"内側の実行回数(loops): {innerLoops:N0}",
            };
            if (inner.ActualTotalTime is { } t) details.Add($"内側 1 回あたり: {t:N3} ms（合計 ≒ {t * innerLoops:N1} ms）");
            if (innerHasSeqScan) details.Add("※ 内側で Seq Scan を繰り返しており特に高コストです。");

            yield return new Finding
            {
                Severity = sev,
                Title = "ネステッドループで内側を多数回実行",
                NodeLabel = node.Label,
                Details = details,
                Suggestion = "外側の各行ごとに内側を実行しています。結合キーにインデックスが無い、または推定行数が小さすぎる可能性があります。"
                           + "結合キーへのインデックス作成、もしくは work_mem 増加によるハッシュ結合への切替を検討してください。",
                SuggestedSql = null,
            };
        }
    }
}
