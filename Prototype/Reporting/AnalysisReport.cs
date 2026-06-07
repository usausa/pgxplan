using System;
using System.Collections.Generic;
using System.Linq;

namespace PgPlanCheck.Reporting;

/// <summary>1 回の実行に関するメタ情報。</summary>
public sealed class RunMetadata
{
    public string Tool { get; init; } = "PgPlanCheck";
    public string Version { get; init; } = "0.2.0";
    public bool Analyzed { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
}

/// <summary>解析結果の全体（構造化出力・CI 判定の単一の真実）。</summary>
public sealed class AnalysisReport
{
    public required RunMetadata Metadata { get; init; }
    public List<SourceResult> Sources { get; } = new();

    /// <summary>ベースラインで抑止した指摘の件数。</summary>
    public int SuppressedCount { get; set; }

    public IEnumerable<(SourceResult Source, StatementResult Statement, Finding Finding)> AllFindings()
    {
        foreach (var s in Sources)
            foreach (var st in s.Statements)
                foreach (var f in st.Findings)
                    yield return (s, st, f);
    }

    public int Count(Severity sev) => AllFindings().Count(x => x.Finding.Severity == sev);
    public int Critical => Count(Severity.Critical);
    public int Warning => Count(Severity.Warning);
    public int Info => Count(Severity.Info);
    public int Errors => Sources.Sum(s => s.Statements.Count(st => st.Error is not null));
    public int StatementCount => Sources.Sum(s => s.Statements.Count);
    public int TotalFindings => Critical + Warning + Info;
}

/// <summary>1 つの入力（ファイル/stdin/インライン SQL）の結果。</summary>
public sealed class SourceResult
{
    public required string Name { get; init; }
    public List<StatementResult> Statements { get; } = new();
}

/// <summary>1 文の結果。スキップ・検証エラー・指摘のいずれかを保持する。</summary>
public sealed class StatementResult
{
    public int Index { get; init; }
    public string Sql { get; init; } = "";
    public bool Skipped { get; init; }
    public string? SkipKeyword { get; init; }
    public string? Error { get; init; }
    public double? PlanningTimeMs { get; init; }
    public double? ExecutionTimeMs { get; init; }
    public List<Finding> Findings { get; set; } = new();
}
