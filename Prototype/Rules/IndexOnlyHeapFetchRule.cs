using System.Collections.Generic;

namespace PgPlanCheck.Rules;

/// <summary>A4: Index Only Scan で Heap Fetches が多い（可視性マップが古い）のを検出し VACUUM を提案する。</summary>
public sealed class IndexOnlyHeapFetchRule : IPlanRule
{
    public string Id => "index-only-heap-fetch";

    private const double FetchThreshold = 1_000;  // この回数以上のヒープ参照で対象に
    private const double FetchFraction = 0.1;      // 出力行数に対する比率

    public IEnumerable<Finding> Analyze(PlanContext ctx)
    {
        if (!ctx.Analyzed) yield break;

        foreach (var nr in ctx.Nodes)
        {
            var node = nr.Node;
            if (node.NodeType != "Index Only Scan") continue;
            if (node.HeapFetches is not { } fetches || fetches <= 0) continue;
            if (fetches < FetchThreshold) continue;

            double rows = (node.ActualRows ?? 0) * node.Loops;
            if (rows > 0 && fetches < rows * FetchFraction) continue;

            var sev = fetches >= 100_000 ? Severity.Warning : Severity.Info;
            var details = new List<string> { $"Heap Fetches: {fetches:N0}" };
            if (rows > 0) details.Add($"出力行数: {rows:N0}");

            yield return new Finding
            {
                Severity = sev,
                Title = "Index Only Scan でのヒープ参照が多い（可視性マップ未更新）",
                NodeLabel = node.Label,
                Details = details,
                Suggestion = "Index Only Scan のはずがヒープを多く参照しており、Index-Only の利点が損なわれています。"
                           + "可視性マップが古い可能性が高いので、対象テーブルへ VACUUM を実行してください（自動 VACUUM の頻度見直しも有効）。",
                SuggestedSql = node.QualifiedRelation is not null ? $"VACUUM (ANALYZE) {node.QualifiedRelation};" : null,
            };
        }
    }
}
