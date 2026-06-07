using System;
using System.IO;

namespace PgPlanCheck.Reporting;

/// <summary>解析結果を 1 つの出力形式へ整形する（D1）。</summary>
public interface IReportWriter
{
    void Write(AnalysisReport report, TextWriter output);
}

public static class ReportWriters
{
    public static IReportWriter For(OutputFormat fmt) => fmt switch
    {
        OutputFormat.Json => new JsonReportWriter(),
        OutputFormat.Markdown => new MarkdownReportWriter(),
        OutputFormat.Sarif => new SarifReportWriter(),
        OutputFormat.JUnit => new JUnitReportWriter(),
        OutputFormat.Cobertura => new CoberturaReportWriter(),
        _ => throw new ArgumentException($"構造化ライターのない形式です: {fmt}"),
    };
}
