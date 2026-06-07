using System.Collections.Generic;

namespace PgPlanCheck.Rules;

/// <summary>A4: JIT コンパイル時間が実行時間に対して大きい（短時間クエリでの逆効果）のを検出する。</summary>
public sealed class JitOverheadRule : IPlanRule
{
    public string Id => "jit-overhead";

    private const double MinJitMs = 5;
    private const double Fraction = 0.2; // 実行時間の 20% 以上を JIT が占める

    public IEnumerable<Finding> Analyze(PlanContext ctx)
    {
        if (!ctx.Analyzed) yield break;

        var jit = ctx.Root.Jit;
        if (jit?.Timing?.Total is not { } total || total < MinJitMs) yield break;

        double exec = ctx.Root.ExecutionTime ?? 0;
        if (exec > 0 && total < exec * Fraction) yield break;

        var details = new List<string> { $"JIT 合計時間: {total:N1} ms" + (exec > 0 ? $"（実行時間の {total / exec:P0}）" : "") };
        if (jit.Functions is { } fn) details.Add($"JIT 関数数: {fn}");

        yield return new Finding
        {
            Severity = Severity.Warning,
            Title = "JIT コンパイルのオーバーヘッド",
            NodeLabel = "（クエリ全体）",
            Details = details,
            Suggestion = "JIT のコンパイル時間が実行時間に対して大きい状態です。短時間・高頻度のクエリでは "
                       + "jit_above_cost / jit_optimize_above_cost を引き上げるか、該当クエリで SET jit = off を検討してください。",
            SuggestedSql = "SET jit = off;  -- セッション単位",
        };
    }
}
