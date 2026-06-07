using System;
using System.Collections.Generic;

namespace PgPlanCheck.Catalog;

/// <summary>1 つのインデックス定義（提案の重複回避に使う）。</summary>
public sealed record IndexInfo(
    string? Schema,
    string Table,
    string Name,
    IReadOnlyList<string> Columns,
    bool IsUnique,
    bool IsPrimary);

/// <summary>1 列の統計（pg_stats 由来。選択度の評価に使う）。</summary>
public sealed record ColumnStats(
    string? Schema,
    string Table,
    string Column,
    double? NDistinct,
    double? Correlation,
    double? NullFraction);

/// <summary>
/// 既存インデックス・列統計の参照口（B1）。実 DB 由来の <see cref="CatalogSnapshot"/> と
/// 空実装 <see cref="NullCatalog"/> を差し替え可能にし、DB 非依存のテストを容易にする。
/// </summary>
public interface ICatalog
{
    /// <summary>指定テーブルのインデックス一覧（無ければ空）。</summary>
    IReadOnlyList<IndexInfo> GetIndexes(string? schema, string table);

    /// <summary>指定列の統計（無ければ null）。</summary>
    ColumnStats? GetColumnStats(string? schema, string table, string column);
}

/// <summary>常に空を返すカタログ（カタログ未取得時の既定）。</summary>
public sealed class NullCatalog : ICatalog
{
    public static readonly NullCatalog Instance = new();
    private static readonly IndexInfo[] None = Array.Empty<IndexInfo>();

    public IReadOnlyList<IndexInfo> GetIndexes(string? schema, string table) => None;
    public ColumnStats? GetColumnStats(string? schema, string table, string column) => null;
}
