using System.Collections.Generic;

namespace PgPlanCheck.Rules;

/// <summary>
/// 実行プランを解析して指摘（<see cref="Finding"/>）を生成する 1 つのルール。
/// 検出ロジックを「1 クラス 1 責務」に分解し、<see cref="RuleRegistry"/> に登録して使う（A1）。
/// 拡張・テスト・ON/OFF を容易にするのが狙い。
/// </summary>
public interface IPlanRule
{
    /// <summary>ルール識別子（ON/OFF 切替やテストでの特定に用いる安定キー）。</summary>
    string Id { get; }

    /// <summary>プラン全体（<see cref="PlanContext"/>）を受け取り、該当する指摘を 0 件以上返す。</summary>
    IEnumerable<Finding> Analyze(PlanContext context);
}
