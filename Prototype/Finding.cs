using System.Collections.Generic;

namespace PgPlanCheck;

public enum Severity
{
    Info,      // 参考情報
    Warning,   // 警告（改善を検討すべき）
    Critical   // 重大（高確率で性能問題）
}

/// <summary>解析で検出した 1 件の指摘。警告内容と改善案をセットで保持する。</summary>
public sealed record Finding
{
    public Severity Severity { get; init; }

    /// <summary>指摘の種別（例: "シーケンシャルスキャン"）。</summary>
    public string Title { get; init; } = "";

    /// <summary>対象ノードのラベル。</summary>
    public string NodeLabel { get; init; } = "";

    /// <summary>根拠となる計測値などの明細行。</summary>
    public List<string> Details { get; init; } = new();

    /// <summary>改善案の説明（なぜ・何をすべきか）。</summary>
    public string? Suggestion { get; init; }

    /// <summary>そのまま実行できる改善用 SQL（任意）。</summary>
    public string? SuggestedSql { get; init; }

    /// <summary>生成元ルールの識別子（オーケストレータが付与）。ON/OFF やテストでの特定に用いる。</summary>
    public string? RuleId { get; init; }
}
