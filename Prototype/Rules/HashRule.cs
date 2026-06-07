using System.Collections.Generic;

namespace PgPlanCheck.Rules;

/// <summary>ハッシュ表が work_mem に収まらず複数バッチに分割（ディスク I/O）したのを検出する。</summary>
public sealed class HashRule : IPlanRule
{
    public string Id => "hash-batches";

    public IEnumerable<Finding> Analyze(PlanContext ctx)
    {
        foreach (var nr in ctx.Nodes)
        {
            var node = nr.Node;
            if (node.NodeType != "Hash") continue;
            if (node.HashBatches is not { } batches || batches <= 1) continue;

            var details = new List<string> { $"Hash Batches: {batches}（1 を超えるとディスク分割）" };
            if (node.OriginalHashBatches is { } orig) details.Add($"計画時バッチ数: {orig}");
            if (node.PeakMemoryUsage is { } mem) details.Add($"ピークメモリ: {mem:N0} kB");

            yield return new Finding
            {
                Severity = Severity.Warning,
                Title = "ハッシュ結合のディスク分割（バッチ）",
                NodeLabel = node.Label,
                Details = details,
                Suggestion = "ハッシュ表が work_mem に収まらず複数バッチに分割され、ディスク I/O が発生しています。"
                           + "work_mem を増やすと 1 バッチで処理でき高速化します。結合元の行数・型幅の見直しも有効です。",
                SuggestedSql = "SET work_mem = '64MB';",
            };
        }
    }
}
