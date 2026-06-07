using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;

namespace PgPlanCheck;

/// <summary>PostgreSQL へ EXPLAIN を発行し、JSON プランを解析して返す。</summary>
public sealed class PgExplainRunner
{
    private readonly NpgsqlConnection _conn;

    public PgExplainRunner(NpgsqlConnection conn) => _conn = conn;

    /// <summary>
    /// 指定文を EXPLAIN で検証する。いずれのモードでも実データは更新しない。
    /// <list type="bullet">
    /// <item><c>analyze=false</c>（既定・検証専用）: プレーン EXPLAIN。クエリを<b>実行せず</b>、
    /// 構文・テーブル/列の存在・型などを検証し、推定プランのみ取得する。READ ONLY トランザクションで実行。</item>
    /// <item><c>analyze=true</c>: EXPLAIN ANALYZE。実測値を得るため実際に実行するが、
    /// トランザクション内で必ず ROLLBACK するため INSERT/UPDATE/DELETE の変更は永続化されない。</item>
    /// </list>
    /// </summary>
    public Task<ExplainRoot?> RunAsync(string statement, bool analyze, CancellationToken ct = default)
        => RunCoreAsync(statement, analyze, null, null, ct);

    /// <summary>
    /// パラメータ化 SQL（<c>$1</c> 等）に値をバインドして EXPLAIN する（C2）。
    /// 値は順番に <c>$1, $2, …</c> へ対応する。実測（analyze）も可能。
    /// </summary>
    public Task<ExplainRoot?> RunAsync(string statement, bool analyze, IReadOnlyList<string>? parameters, CancellationToken ct = default)
        => RunCoreAsync(statement, analyze, null, parameters, ct);

    /// <summary>
    /// 仮想インデックスを「トランザクション内で実際に作成」した上で EXPLAIN し、結果を返す。
    /// 最後に必ず ROLLBACK するため作成したインデックスは破棄され、実 DB は変更されない（What-if 用）。
    /// </summary>
    public Task<ExplainRoot?> RunWithIndexesAsync(string statement, bool analyze, IReadOnlyList<string> indexes, CancellationToken ct = default)
        => RunCoreAsync(statement, analyze, indexes, null, ct);

    /// <summary>HypoPG 拡張が利用可能か（B3）。</summary>
    public async Task<bool> HasHypoPgAsync(CancellationToken ct = default)
    {
        try
        {
            await using var cmd = new NpgsqlCommand("SELECT 1 FROM pg_extension WHERE extname = 'hypopg'", _conn);
            return await cmd.ExecuteScalarAsync(ct) is not null;
        }
        catch (PostgresException)
        {
            return false;
        }
    }

    /// <summary>
    /// HypoPG の仮想インデックスを使って What-if（B3）。実体を作らずプランナのみに見せるため
    /// 大表でも瞬時・ゼロ書込。ただし実行はできないため推定コストのみ（時間は得られない）。
    /// </summary>
    public async Task<ExplainRoot?> RunWithHypotheticalAsync(string statement, IReadOnlyList<string> indexes, CancellationToken ct = default)
    {
        try
        {
            foreach (var ddl in indexes)
            {
                await using var c = new NpgsqlCommand("SELECT hypopg_create_index(@ddl)", _conn);
                c.Parameters.AddWithValue("ddl", FirstStatement(ddl));
                await c.ExecuteNonQueryAsync(ct);
            }

            var sb = new StringBuilder();
            await using (var cmd = new NpgsqlCommand($"EXPLAIN (VERBOSE, FORMAT JSON) {statement}", _conn))
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                    sb.Append(reader.GetFieldValue<string>(0));
            }

            var roots = JsonSerializer.Deserialize<List<ExplainRoot>>(sb.ToString());
            return roots is { Count: > 0 } ? roots[0] : null;
        }
        finally
        {
            await using var reset = new NpgsqlCommand("SELECT hypopg_reset()", _conn);
            try { await reset.ExecuteNonQueryAsync(ct); } catch (PostgresException) { /* 無視 */ }
        }
    }

    private async Task<ExplainRoot?> RunCoreAsync(
        string statement, bool analyze, IReadOnlyList<string>? hypotheticalIndexes,
        IReadOnlyList<string>? parameters, CancellationToken ct)
    {
        string options = analyze ? "ANALYZE, BUFFERS, VERBOSE, FORMAT JSON" : "VERBOSE, FORMAT JSON";

        await using var tx = await _conn.BeginTransactionAsync(ct);
        try
        {
            // 純粋な検証（実行なし・仮インデックスなし）のときは READ ONLY を強制して二重に安全側へ。
            if (hypotheticalIndexes is null && !analyze)
            {
                await using var ro = new NpgsqlCommand("SET TRANSACTION READ ONLY", _conn, tx);
                await ro.ExecuteNonQueryAsync(ct);
            }

            // What-if: 仮インデックスを作成（このトランザクション内でのみ有効。ROLLBACK で破棄）。
            if (hypotheticalIndexes is not null)
            {
                foreach (var ddl in hypotheticalIndexes)
                {
                    await using var c = new NpgsqlCommand(ddl, _conn, tx) { CommandTimeout = 300 };
                    await c.ExecuteNonQueryAsync(ct);
                }
            }

            var sb = new StringBuilder();
            await using (var cmd = new NpgsqlCommand($"EXPLAIN ({options}) {statement}", _conn, tx))
            {
                if (parameters is not null)
                    foreach (var p in parameters)
                        // Unknown 型で送り、対象列の型（bigint 等）にサーバ側で合わせさせる
                        // （文字列リテラルと同じ型強制。text 固定だと bigint=text で型エラーになる）。
                        cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)p ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Unknown });

                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    sb.Append(reader.GetFieldValue<string>(0));
            }

            var roots = JsonSerializer.Deserialize<List<ExplainRoot>>(sb.ToString());
            return roots is { Count: > 0 } ? roots[0] : null;
        }
        catch
        {
            // 失敗時はロールバックを試みる（失敗状態のトランザクションでは例外になり得るため握りつぶす）。
            // 元の例外をそのまま呼び出し側へ伝える。成功時は using の破棄でロールバックされる。
            try { await tx.RollbackAsync(ct); } catch { /* 無視 */ }
            throw;
        }
    }

    /// <summary>提案 SQL（補足コメントが続くことがある）から先頭の 1 文だけを取り出す。</summary>
    private static string FirstStatement(string ddl)
    {
        int nl = ddl.IndexOf('\n');
        var s = (nl >= 0 ? ddl.Substring(0, nl) : ddl).Trim();
        return s.TrimEnd(';').Trim();
    }
}
