using System.Collections.Generic;
using System.Linq;

namespace PgPlanCheck.Rules;

/// <summary>
/// A3: インデックスが効かない（sargable でない）書き方をスキャンノードの Filter から検出する。
/// 型不一致キャスト・列への関数適用・先頭ワイルドカードの LIKE が対象。構造的に判定できるため
/// 検証専用モードでも動作する。
/// </summary>
public sealed class SargabilityRule : IPlanRule
{
    public string Id => "sargability";

    private static readonly HashSet<string> ScanNodes = new(System.StringComparer.Ordinal)
    {
        "Seq Scan", "Index Scan", "Index Only Scan", "Bitmap Heap Scan",
    };

    public IEnumerable<Finding> Analyze(PlanContext ctx)
    {
        foreach (var nr in ctx.Nodes)
        {
            var node = nr.Node;
            if (node.Filter is null || !ScanNodes.Contains(node.NodeType)) continue;

            var issues = FilterColumnExtractor.DetectSargability(node.Filter);
            foreach (var grp in issues.GroupBy(i => i.Kind))
                yield return BuildFinding(node, grp.Key, grp.Select(i => i.Column).Distinct().ToList());
        }
    }

    private static Finding BuildFinding(PlanNode node, SargabilityIssueKind kind, List<string> cols)
    {
        string colList = string.Join(", ", cols);
        string col = cols[0];
        string? qual = node.QualifiedRelation;
        string rel = node.RelationName ?? "t";

        return kind switch
        {
            SargabilityIssueKind.ColumnCast => new Finding
            {
                Severity = Severity.Warning,
                Title = "インデックス非効率: 型不一致のキャスト",
                NodeLabel = node.Label,
                Details = new List<string> { $"Filter: {node.Filter}", $"対象列: {colList}" },
                Suggestion = $"列 {col} に型キャストが入っているため、{col} のインデックスが使えず全件走査になりがちです。"
                           + "比較相手の値・パラメータを列と同じ型に揃える（列ではなくリテラル側で型を合わせる）か、必要なら式インデックスを検討してください。",
                SuggestedSql = null,
            },
            SargabilityIssueKind.FunctionOnColumn => new Finding
            {
                Severity = Severity.Warning,
                Title = "インデックス非効率: 列への関数適用",
                NodeLabel = node.Label,
                Details = new List<string> { $"Filter: {node.Filter}", $"対象列: {colList}" },
                Suggestion = $"列 {col} に関数を適用しているため、通常のインデックスが使えません。"
                           + "関数を使わない sargable な条件へ書き換えるか、式インデックスを作成してください。",
                SuggestedSql = qual is null ? null
                    : $"-- 例: 式インデックス（実際に適用している関数に合わせて調整）\n"
                    + $"CREATE INDEX idx_{rel}_{col}_expr ON {qual} ((/* 関数 */({col})));",
            },
            SargabilityIssueKind.LeadingWildcard => new Finding
            {
                Severity = Severity.Warning,
                Title = "インデックス非効率: 先頭ワイルドカードの LIKE",
                NodeLabel = node.Label,
                Details = new List<string> { $"Filter: {node.Filter}", $"対象列: {colList}" },
                Suggestion = $"先頭が % のパターン一致（列 {col}）は B-Tree インデックスで絞り込めません。"
                           + "前方一致（'abc%'）に変えられないか検討し、部分一致が必須なら pg_trgm の GIN インデックスを利用してください。",
                SuggestedSql = qual is null ? null
                    : $"CREATE EXTENSION IF NOT EXISTS pg_trgm;\n"
                    + $"CREATE INDEX idx_{rel}_{col}_trgm ON {qual} USING gin ({col} gin_trgm_ops);",
            },
            _ => new Finding
            {
                Severity = Severity.Info,
                Title = "インデックス非効率",
                NodeLabel = node.Label,
                Details = new List<string> { $"Filter: {node.Filter}" },
            },
        };
    }
}
