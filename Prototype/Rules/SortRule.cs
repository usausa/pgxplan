using System;
using System.Collections.Generic;

namespace PgPlanCheck.Rules;

/// <summary>ディスクへの外部ソート（Sort Method: external …）を検出する。</summary>
public sealed class SortRule : IPlanRule
{
    public string Id => "external-sort";

    public IEnumerable<Finding> Analyze(PlanContext ctx)
    {
        foreach (var nr in ctx.Nodes)
        {
            var node = nr.Node;
            if (node.NodeType is not ("Sort" or "Incremental Sort")) continue;
            if (node.SortMethod is null) continue;
            if (node.SortMethod.IndexOf("external", StringComparison.OrdinalIgnoreCase) < 0) continue;

            var details = new List<string> { $"Sort Method: {node.SortMethod}" };
            if (node.SortSpaceUsed is { } kb) details.Add($"使用領域: {kb:N0} kB ({node.SortSpaceType})");
            if (node.SortKey is { Count: > 0 }) details.Add($"Sort Key: {string.Join(", ", node.SortKey)}");

            yield return new Finding
            {
                Severity = Severity.Warning,
                Title = "ディスクへの外部ソート",
                NodeLabel = node.Label,
                Details = details,
                Suggestion = "ソート対象が work_mem に収まらずディスクを使用しています。work_mem を増やすとメモリ内ソートになり高速化します。"
                           + "ORDER BY 列に一致するインデックスがあればソート自体を回避できます（特に LIMIT 併用時に有効）。",
                SuggestedSql = "SET work_mem = '64MB';  -- セッション単位。恒久化は postgresql.conf / ALTER SYSTEM",
            };
        }
    }
}
