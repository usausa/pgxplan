using System.Collections.Generic;
using System.Linq;

namespace PgPlanCheck.Rules;

/// <summary>A4: DML に付随するトリガの実行時間が支配的なのを検出する。</summary>
public sealed class TriggerTimeRule : IPlanRule
{
    public string Id => "trigger-time";

    private const double MinMs = 5;
    private const double Fraction = 0.2;

    public IEnumerable<Finding> Analyze(PlanContext ctx)
    {
        if (!ctx.Analyzed) yield break;

        var triggers = ctx.Root.Triggers;
        if (triggers is null || triggers.Count == 0) yield break;

        double total = triggers.Sum(t => t.Time ?? 0);
        if (total < MinMs) yield break;

        double exec = ctx.Root.ExecutionTime ?? 0;
        if (exec > 0 && total < exec * Fraction) yield break;

        var details = new List<string> { $"トリガ合計時間: {total:N1} ms" + (exec > 0 ? $"（実行時間の {total / exec:P0}）" : "") };
        foreach (var t in triggers.OrderByDescending(t => t.Time ?? 0).Take(5))
            details.Add($"{t.Name}{(t.Relation is not null ? $" on {t.Relation}" : "")}: {t.Time:N1} ms / {t.Calls:N0} 回");

        yield return new Finding
        {
            Severity = Severity.Warning,
            Title = "トリガ実行時間が大きい（DML）",
            NodeLabel = "（トリガ）",
            Details = details,
            Suggestion = "DML に付随するトリガの実行時間が支配的です。トリガ内クエリの最適化、行単位→文単位トリガ化、"
                       + "不要トリガや制約トリガの見直しを検討してください。",
            SuggestedSql = null,
        };
    }
}
