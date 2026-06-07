using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace PgPlanCheck.Catalog;

/// <summary>
/// 接続先 DB の pg_index / pg_stats を読み出して <see cref="CatalogSnapshot"/> を作る（B1）。
/// プランで参照しているテーブルに限定して取得する。
/// </summary>
public static class PgCatalogLoader
{
    public static async Task<CatalogSnapshot> LoadAsync(
        NpgsqlConnection conn,
        IEnumerable<(string? Schema, string Table)> relations,
        CancellationToken ct = default)
    {
        var tables = relations
            .Select(r => r.Table)
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var indexes = new List<IndexInfo>();
        var stats = new List<ColumnStats>();
        if (tables.Length == 0) return new CatalogSnapshot(indexes, stats);

        const string idxSql = @"
SELECT n.nspname AS schemaname,
       t.relname AS tablename,
       ic.relname AS indexname,
       ix.indisunique AS is_unique,
       ix.indisprimary AS is_primary,
       (SELECT array_agg(a.attname ORDER BY k.ord)
          FROM unnest(ix.indkey) WITH ORDINALITY AS k(attnum, ord)
          JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum
         WHERE k.attnum <> 0) AS columns
FROM pg_index ix
JOIN pg_class ic    ON ic.oid = ix.indexrelid
JOIN pg_class t     ON t.oid  = ix.indrelid
JOIN pg_namespace n ON n.oid  = t.relnamespace
WHERE t.relname = ANY(@tables)
  AND n.nspname NOT IN ('pg_catalog', 'information_schema');";

        await using (var cmd = new NpgsqlCommand(idxSql, conn))
        {
            cmd.Parameters.AddWithValue("tables", tables);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var cols = r.IsDBNull(5) ? Array.Empty<string>() : r.GetFieldValue<string[]>(5);
                indexes.Add(new IndexInfo(
                    Schema: r.GetString(0),
                    Table: r.GetString(1),
                    Name: r.GetString(2),
                    Columns: cols.ToList(),
                    IsUnique: r.GetBoolean(3),
                    IsPrimary: r.GetBoolean(4)));
            }
        }

        const string statSql = @"
SELECT schemaname, tablename, attname, n_distinct, correlation, null_frac
FROM pg_stats
WHERE tablename = ANY(@tables);";

        await using (var cmd = new NpgsqlCommand(statSql, conn))
        {
            cmd.Parameters.AddWithValue("tables", tables);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                stats.Add(new ColumnStats(
                    Schema: r.GetString(0),
                    Table: r.GetString(1),
                    Column: r.GetString(2),
                    NDistinct: ReadReal(r, 3),
                    Correlation: ReadReal(r, 4),
                    NullFraction: ReadReal(r, 5)));
            }
        }

        return new CatalogSnapshot(indexes, stats);
    }

    // pg_stats の n_distinct / correlation / null_frac は real(float4)。double へ昇格して返す。
    private static double? ReadReal(NpgsqlDataReader r, int ordinal)
        => r.IsDBNull(ordinal) ? null : r.GetFieldValue<float>(ordinal);
}
