using System;
using System.Collections.Generic;
using System.Linq;

namespace PgPlanCheck.Rules;

/// <summary>
/// 推定行数と実測行数が大きく乖離しているノードを検出する（実測モードのみ）。
/// A5: 過大推定の除外は「LIMIT がそのノードの祖先にある場合」に限定し、過小推定で複数列に
/// 条件があるときは列相関とみなして拡張統計（CREATE STATISTICS）を列名つきで提案する。
/// </summary>
public sealed class EstimationErrorRule : IPlanRule
{
    public string Id => "estimation-error";

    private const double MinRows = 100; // 推定乖離を見る最小行数
    private const double Ratio = 10;    // 推定と実測が 10 倍以上ずれたら指摘

    public IEnumerable<Finding> Analyze(PlanContext ctx)
    {
        if (!ctx.Analyzed) yield break;

        var candidates = new List<(double ratio, PlanNode node, bool under)>();
        foreach (var nr in ctx.Nodes)
        {
            var node = nr.Node;
            if (node.ActualRows is not { } actual) continue;
            double plan = node.PlanRows;
            double hi = Math.Max(plan, actual);
            double lo = Math.Max(1, Math.Min(plan, actual));
            double ratio = hi / lo;
            if (hi < MinRows || ratio < Ratio) continue;

            bool under = actual > plan;
            // A5: LIMIT は配下ノードの実測行数を途中で打ち切る（過大推定に見える）。
            // 統計の問題ではないため、LIMIT を祖先に持つノードの「過大推定」だけを除外する。
            if (!under && ctx.Ancestors(node).Any(a => a.NodeType == "Limit")) continue;

            candidates.Add((ratio, node, under));
        }

        foreach (var (ratio, node, under) in candidates.OrderByDescending(c => c.ratio).Take(3))
        {
            var sev = ratio >= 100 ? Severity.Warning : Severity.Info;
            var dir = under ? "過小推定（実測がはるかに多い）" : "過大推定（実測がはるかに少ない）";
            var details = new List<string>
            {
                $"推定: {node.PlanRows:N0} 行 / 実測: {node.ActualRows:N0} 行（約 {ratio:N0} 倍の乖離・{dir}）",
            };
            if (node.Filter is not null) details.Add($"Filter: {node.Filter}");

            var cols = node.Filter is not null ? FilterColumnExtractor.Extract(node.Filter) : new List<string>();
            bool multiColumn = under && cols.Count >= 2 && node.RelationName is not null;

            string? sql;
            if (multiColumn)
            {
                var colList = string.Join(", ", cols);
                var statName = $"stx_{node.RelationName}_{string.Join("_", cols)}";
                sql = $"CREATE STATISTICS {statName} (dependencies, ndistinct) ON {colList} FROM {node.QualifiedRelation};\n"
                    + $"ANALYZE {node.QualifiedRelation};";
                details.Add($"複数列条件（{colList}）の相関により行数推定がずれている可能性があります。");
            }
            else
            {
                sql = node.RelationName is not null ? $"ANALYZE {node.QualifiedRelation};" : null;
            }

            yield return new Finding
            {
                Severity = sev,
                Title = "推定行数と実測の大きな乖離",
                NodeLabel = node.Label,
                Details = details,
                Suggestion = multiColumn
                    ? "複数列の相関で行数推定がずれています。該当列に拡張統計（CREATE STATISTICS）を作成して ANALYZE すると改善します。"
                      + "誤った推定は結合方式の誤選択（例: ネステッドループ採用）を招きます。"
                    : "統計情報が古いか、条件の相関により行数推定がずれています。対象テーブルへ ANALYZE を実行してください。"
                      + "相関する複数列の条件には拡張統計 CREATE STATISTICS も有効です。",
                SuggestedSql = sql,
            };
        }
    }
}
