using System;
using System.Collections.Generic;
using System.Linq;

namespace PgPlanCheck.Rules;

/// <summary>
/// 既定で実行するルールの一覧（A1）。並び順は出力時の検出順（重大度が同じ場合）に対応する。
/// 新しいルールはここへ 1 行追加するだけで有効化できる。
/// </summary>
public static class RuleRegistry
{
    public static IReadOnlyList<IPlanRule> Default { get; } = new IPlanRule[]
    {
        new SeqScanRule(),             // 全件走査
        new SargabilityRule(),         // A3: インデックス非効率な書き方
        new SortRule(),                // 外部ソート
        new HashRule(),                // ハッシュバッチ分割
        new BitmapHeapRule(),          // ビットマップ lossy 化
        new IndexFilterRule(),         // インデックス後の大量フィルタ
        new NestedLoopRule(),          // ネステッドループ過多
        new CartesianProductRule(),    // A4: 直積（結合条件なし）
        new CorrelatedSubplanRule(),   // A4: 相関サブプラン多数実行
        new IndexOnlyHeapFetchRule(),  // A4: Index-Only の Heap Fetches
        new ParallelWorkersRule(),     // A4: 並列ワーカー不足
        new JitOverheadRule(),         // A4: JIT オーバーヘッド
        new TriggerTimeRule(),         // A4: トリガ時間
        new BufferUsageRule(),         // A2: 一時 I/O・キャッシュミス
        new EstimationErrorRule(),     // A5: 推定乖離（拡張統計の提案つき）
        new BottleneckRule(),          // 時間集中点
    };

    /// <summary>すべてのルール ID。</summary>
    public static IReadOnlyList<string> AllIds => Default.Select(r => r.Id).ToList();

    /// <summary>
    /// 設定に従って有効なルールを選ぶ。<paramref name="enabled"/> が空でなければホワイトリスト、
    /// それ以外は全ルールから <paramref name="disabled"/> を除外する。未知の ID は <paramref name="unknown"/> に返す。
    /// </summary>
    public static IReadOnlyList<IPlanRule> Resolve(
        IReadOnlyCollection<string>? enabled,
        IReadOnlyCollection<string>? disabled,
        out IReadOnlyList<string> unknown)
    {
        var known = new HashSet<string>(AllIds, StringComparer.OrdinalIgnoreCase);
        var unk = new List<string>();
        foreach (var id in (enabled ?? Array.Empty<string>()).Concat(disabled ?? Array.Empty<string>()))
            if (!known.Contains(id) && !unk.Contains(id, StringComparer.OrdinalIgnoreCase))
                unk.Add(id);
        unknown = unk;

        if (enabled is { Count: > 0 })
            return Default.Where(r => enabled.Contains(r.Id, StringComparer.OrdinalIgnoreCase)).ToList();
        if (disabled is { Count: > 0 })
            return Default.Where(r => !disabled.Contains(r.Id, StringComparer.OrdinalIgnoreCase)).ToList();
        return Default;
    }
}
