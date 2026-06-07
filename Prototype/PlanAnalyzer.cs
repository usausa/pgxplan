using System.Collections.Generic;
using System.Linq;
using PgPlanCheck.Catalog;
using PgPlanCheck.Rules;

namespace PgPlanCheck;

/// <summary>
/// 登録された各 <see cref="IPlanRule"/> を実行し、指摘（<see cref="Finding"/>）を集約するオーケストレータ。
/// 検出ロジック本体は Rules/ 配下の各ルールへ分割されている（A1: ルールのプラグイン化）。
/// </summary>
public sealed class PlanAnalyzer
{
    private readonly IReadOnlyList<IPlanRule> _rules;

    public PlanAnalyzer() : this(RuleRegistry.Default) { }

    /// <summary>使用するルール集合を差し替えて生成する（テスト・ON/OFF 用）。</summary>
    public PlanAnalyzer(IReadOnlyList<IPlanRule> rules) => _rules = rules;

    public IReadOnlyList<Finding> Analyze(ExplainRoot root, bool analyzed)
        => Analyze(root, analyzed, NullCatalog.Instance);

    public IReadOnlyList<Finding> Analyze(ExplainRoot root, bool analyzed, ICatalog? catalog)
    {
        var ctx = PlanContext.Build(root, analyzed, catalog);

        var findings = new List<Finding>();
        foreach (var rule in _rules)
            foreach (var f in rule.Analyze(ctx))
                findings.Add(f.RuleId is null ? f with { RuleId = rule.Id } : f);

        // 重大度の高い順に並べ替え（同順位は検出順を保つ）。
        return findings
            .Select((f, idx) => (f, idx))
            .OrderByDescending(x => (int)x.f.Severity)
            .ThenBy(x => x.idx)
            .Select(x => x.f)
            .ToList();
    }
}
