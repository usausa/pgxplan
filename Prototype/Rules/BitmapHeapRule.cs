using System.Collections.Generic;

namespace PgPlanCheck.Rules;

/// <summary>ビットマップが work_mem を超えて lossy 化し、ヒープ再チェックが発生したのを検出する。</summary>
public sealed class BitmapHeapRule : IPlanRule
{
    public string Id => "bitmap-lossy";

    public IEnumerable<Finding> Analyze(PlanContext ctx)
    {
        if (!ctx.Analyzed) yield break;

        foreach (var nr in ctx.Nodes)
        {
            var node = nr.Node;
            if (node.NodeType != "Bitmap Heap Scan") continue;
            if (node.LossyHeapBlocks is not { } lossy || lossy <= 0) continue;

            var details = new List<string> { $"Lossy Heap Blocks: {lossy:N0}（精度を落としたブロック）" };
            if (node.ExactHeapBlocks is { } exact) details.Add($"Exact Heap Blocks: {exact:N0}");
            if (node.RowsRemovedByIndexRecheck is { } rc) details.Add($"再チェックで除外: {rc:N0} 行");

            yield return new Finding
            {
                Severity = Severity.Warning,
                Title = "ビットマップが work_mem 超過（lossy 化）",
                NodeLabel = node.Label,
                Details = details,
                Suggestion = "ビットマップが work_mem に収まらず lossy 化し、ヒープ再チェックの負荷が発生しています。"
                           + "work_mem を増やすと精度が上がり再チェックが減ります。",
                SuggestedSql = "SET work_mem = '64MB';",
            };
        }
    }
}
