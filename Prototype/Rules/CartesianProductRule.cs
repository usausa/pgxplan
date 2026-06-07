using System.Collections.Generic;
using System.Linq;

namespace PgPlanCheck.Rules;

/// <summary>A4: 結合条件のない Nested Loop（直積＝CROSS JOIN の可能性）を検出する。</summary>
public sealed class CartesianProductRule : IPlanRule
{
    public string Id => "cartesian-join";

    private const double MinRows = 1_000;

    public IEnumerable<Finding> Analyze(PlanContext ctx)
    {
        foreach (var nr in ctx.Nodes)
        {
            var node = nr.Node;
            if (node.NodeType != "Nested Loop") continue;
            if (node.JoinFilter is not null) continue;        // 明示の結合条件あり
            if (node.Plans is not { Count: >= 2 }) continue;

            var inner = node.Plans.FirstOrDefault(p => p.ParentRelationship == "Inner") ?? node.Plans[^1];
            // 内側が外側を参照（Index Cond）していれば通常の結合とみなす
            if (PlanWalker.Walk(inner).Any(x => x.Node.IndexCond is not null)) continue;

            double rows = ctx.Analyzed ? (node.ActualRows ?? node.PlanRows) : node.PlanRows;
            if (rows < MinRows) continue;

            yield return new Finding
            {
                Severity = Severity.Warning,
                Title = "結合条件のない Nested Loop（直積の可能性）",
                NodeLabel = node.Label,
                Details = new List<string>
                {
                    $"内側: {inner.Label}",
                    $"出力行数: {rows:N0}（Join Filter / Index Cond なし）",
                },
                Suggestion = "結合条件が見当たらず、直積（CROSS JOIN）になっている可能性があります。"
                           + "ON 句の結合条件が欠けていないか、意図した CROSS JOIN かを確認してください。",
                SuggestedSql = null,
            };
        }
    }
}
