using System.Collections.Generic;
using System.Linq;

namespace PgPlanCheck.Rules;

/// <summary>
/// A2: BUFFERS（取得済みデータ）から、一時ファイル I/O（work_mem 超過の spill）と
/// ディスク読み込み過多（キャッシュミス）を可視化する。実測モードのみ。
/// </summary>
public sealed class BufferUsageRule : IPlanRule
{
    public string Id => "buffer-usage";

    private const long BlockBytes = 8192;
    private const long TempBlocksThreshold = 128;     // 約 1 MB 以上の一時 I/O で指摘
    private const long ReadBlocksThreshold = 12_800;  // 約 100 MB 以上のディスク読み込みで指摘
    private const double LowHitRatio = 0.9;

    public IEnumerable<Finding> Analyze(PlanContext ctx)
    {
        if (!ctx.Analyzed) yield break;
        var root = ctx.Root.Plan;

        // --- 一時ファイル I/O（ソート/ハッシュ/集約などの spill）---
        // BUFFERS の各値は子を含む累計のため、ルートにクエリ全体の合計が乗る。
        long tempTotal = (root.TempReadBlocks ?? 0) + (root.TempWrittenBlocks ?? 0);
        if (tempTotal >= TempBlocksThreshold)
        {
            var spillers = ctx.Nodes
                .Select(n => (n.Node, self: SelfTempBlocks(n.Node)))
                .Where(x => x.self > 0)
                .OrderByDescending(x => x.self)
                .Take(5)
                .ToList();

            var details = new List<string>
            {
                $"一時ファイル I/O 合計: 約 {Mb(tempTotal):N1} MB"
                + $"（読み {Mb(root.TempReadBlocks ?? 0):N1} / 書き {Mb(root.TempWrittenBlocks ?? 0):N1} MB）",
            };
            foreach (var s in spillers)
                details.Add($"spill: {s.Node.Label} — 約 {Mb(s.self):N1} MB");

            yield return new Finding
            {
                Severity = Severity.Warning,
                Title = "一時ファイル I/O の発生（work_mem 超過）",
                NodeLabel = spillers.Count > 0 ? spillers[0].Node.Label : root.Label,
                Details = details,
                Suggestion = "ソート・ハッシュ・集約などが work_mem に収まらずディスクへ書き出しています。"
                           + "work_mem を増やすと一時ファイル I/O を削減できます（セッション単位での調整が可能）。",
                SuggestedSql = "SET work_mem = '64MB';",
            };
        }

        // --- ディスク読み込み過多（キャッシュミス）---
        long hit = root.SharedHitBlocks ?? 0;
        long read = root.SharedReadBlocks ?? 0;
        if (read >= ReadBlocksThreshold)
        {
            double ratio = (hit + read) > 0 ? (double)hit / (hit + read) : 1d;
            if (ratio < LowHitRatio)
            {
                var details = new List<string>
                {
                    $"共有バッファ読み込み: 約 {Mb(read):N1} MB（{read:N0} ブロック）",
                    $"バッファヒット率: {ratio:P1}（hit {hit:N0} / read {read:N0}）",
                };
                if (root.IoReadTime is { } iort && iort > 0) details.Add($"I/O 読み込み時間: {iort:N1} ms");

                yield return new Finding
                {
                    Severity = Severity.Info,
                    Title = "ディスク読み込みが多い（キャッシュミス）",
                    NodeLabel = root.Label,
                    Details = details,
                    Suggestion = "多くのブロックをディスクから読み込んでいます。初回実行ではキャッシュが温まっておらず Read が増えますが、"
                               + "恒常的であれば、絞り込みインデックスで読み取りブロックを減らす・shared_buffers の見直し・pg_prewarm などを検討してください。",
                    SuggestedSql = null,
                };
            }
        }
    }

    /// <summary>このノード単体の一時ブロック数（累計から直接の子の累計を差し引いた概算）。</summary>
    private static long SelfTempBlocks(PlanNode n)
    {
        long v = (n.TempReadBlocks ?? 0) + (n.TempWrittenBlocks ?? 0);
        if (n.Plans is not null)
            foreach (var c in n.Plans)
                v -= (c.TempReadBlocks ?? 0) + (c.TempWrittenBlocks ?? 0);
        return v > 0 ? v : 0;
    }

    private static double Mb(long blocks) => blocks * (double)BlockBytes / (1024 * 1024);
}
