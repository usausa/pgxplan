using System.Collections.Generic;

namespace PgPlanCheck.Rules;

/// <summary>インデックスで絞り込んだ後、Filter でさらに大量の行を捨てているノードを検出する。</summary>
public sealed class IndexFilterRule : IPlanRule
{
    public string Id => "index-filter";

    private const double IndexFilterRemoved = 10_000; // インデックス後のフィルタで捨てる行数

    public IEnumerable<Finding> Analyze(PlanContext ctx)
    {
        if (!ctx.Analyzed) yield break;

        foreach (var nr in ctx.Nodes)
        {
            var node = nr.Node;
            if (node.NodeType is not ("Bitmap Heap Scan" or "Index Scan" or "Index Only Scan")) continue;
            if (node.Filter is null) continue;

            double loops = node.Loops;
            double removed = (node.RowsRemovedByFilter ?? 0) * loops;
            double outRows = (node.ActualRows ?? 0) * loops;
            if (removed < IndexFilterRemoved || removed < outRows) continue;

            var cols = FilterColumnExtractor.Extract(node.Filter);
            string? sql = null;
            if (cols.Count > 0 && node.RelationName is not null)
            {
                sql = $"-- 例: 既存インデックスの先頭列に {string.Join(", ", cols)} を加えた複合インデックスを検討\n"
                    + $"CREATE INDEX idx_{node.RelationName}_composite ON {node.QualifiedRelation} (/* 既存キー */, {string.Join(", ", cols)});";
            }

            yield return new Finding
            {
                Severity = Severity.Info,
                Title = "インデックス走査後のフィルタで大量除外",
                NodeLabel = node.Label,
                Details = new List<string>
                {
                    $"Index Cond: {node.IndexCond ?? "(なし)"}",
                    $"Filter: {node.Filter}",
                    $"フィルタで除外: {removed:N0} 行（出力 {outRows:N0} 行）",
                },
                Suggestion = "インデックスで絞り込んだ後、Filter で多数の行を捨てています。"
                           + "フィルタ列をインデックスに含めた複合インデックスにすると、さらに絞り込めます。",
                SuggestedSql = sql,
            };
        }
    }
}
