using System;
using System.Collections.Generic;
using System.Linq;

namespace PgPlanCheck.Catalog;

/// <summary>
/// DB から一度読み出したインデックス・統計をメモリ上に保持する不変カタログ。
/// テストでは固定データを直接渡してインスタンス化できる（DB 不要）。
/// </summary>
public sealed class CatalogSnapshot : ICatalog
{
    private readonly ILookup<string, IndexInfo> _indexesByTable;
    private readonly Dictionary<string, ColumnStats> _stats;

    public CatalogSnapshot(IEnumerable<IndexInfo> indexes, IEnumerable<ColumnStats> stats)
    {
        _indexesByTable = indexes.ToLookup(i => i.Table, StringComparer.OrdinalIgnoreCase);
        _stats = new Dictionary<string, ColumnStats>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in stats)
            _stats[StatKey(s.Schema, s.Table, s.Column)] = s;
    }

    public IReadOnlyList<IndexInfo> GetIndexes(string? schema, string table)
    {
        var all = _indexesByTable[table].ToList();
        if (schema is null || all.Count == 0) return all;
        // スキーマが分かる場合は一致するものを優先（同名テーブルの取り違え防止）。一致が無ければ全件。
        var bySchema = all.Where(i => string.Equals(i.Schema, schema, StringComparison.OrdinalIgnoreCase)).ToList();
        return bySchema.Count > 0 ? bySchema : all;
    }

    public ColumnStats? GetColumnStats(string? schema, string table, string column)
    {
        if (schema is not null && _stats.TryGetValue(StatKey(schema, table, column), out var exact))
            return exact;
        // スキーマ不明・不一致時はテーブル名＋列名で探す。
        return _stats.Values.FirstOrDefault(v =>
            string.Equals(v.Table, table, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(v.Column, column, StringComparison.OrdinalIgnoreCase));
    }

    private static string StatKey(string? schema, string table, string column)
        => $"{schema?.ToLowerInvariant()}{table.ToLowerInvariant()}{column.ToLowerInvariant()}";
}
