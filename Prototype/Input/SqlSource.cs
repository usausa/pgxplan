namespace PgPlanCheck.Input;

/// <summary>解析対象の 1 入力。名前（表示・フィンガープリント用）と SQL 本文を持つ。</summary>
public sealed record SqlSource(string Name, string Sql);
