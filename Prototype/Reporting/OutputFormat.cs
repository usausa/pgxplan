using System;

namespace PgPlanCheck.Reporting;

/// <summary>レポート出力形式（D1）。</summary>
public enum OutputFormat { Console, Json, Markdown, Sarif, JUnit, Cobertura }

/// <summary>CI ゲートの失敗しきい値（D2）。</summary>
public enum FailOnLevel { None, Info, Warning, Critical }

public static class FormatParsing
{
    public static bool TryParseFormat(string s, out OutputFormat fmt)
    {
        switch (s.Trim().ToLowerInvariant())
        {
            case "console": case "text": fmt = OutputFormat.Console; return true;
            case "json": fmt = OutputFormat.Json; return true;
            case "markdown": case "md": fmt = OutputFormat.Markdown; return true;
            case "sarif": fmt = OutputFormat.Sarif; return true;
            case "junit": case "xml": fmt = OutputFormat.JUnit; return true;
            case "cobertura": case "coverage": fmt = OutputFormat.Cobertura; return true;
            default: fmt = OutputFormat.Console; return false;
        }
    }

    public static bool TryParseFailOn(string s, out FailOnLevel lvl)
    {
        switch (s.Trim().ToLowerInvariant())
        {
            case "none": case "off": lvl = FailOnLevel.None; return true;
            case "info": lvl = FailOnLevel.Info; return true;
            case "warning": case "warn": lvl = FailOnLevel.Warning; return true;
            case "critical": case "error": lvl = FailOnLevel.Critical; return true;
            default: lvl = FailOnLevel.Warning; return false;
        }
    }
}

/// <summary>重大度の表記ヘルパー（日本語ラベル・SARIF level・GitHub annotation 種別）。</summary>
public static class SeverityText
{
    public static string Ja(Severity s) => s switch
    {
        Severity.Critical => "重大",
        Severity.Warning => "警告",
        _ => "情報",
    };

    public static string Sarif(Severity s) => s switch
    {
        Severity.Critical => "error",
        Severity.Warning => "warning",
        _ => "note",
    };

    public static string GitHub(Severity s) => s switch
    {
        Severity.Critical => "error",
        Severity.Warning => "warning",
        _ => "notice",
    };
}

/// <summary>CI ゲート判定（D2）。</summary>
public static class CiGate
{
    /// <summary>指定しきい値以上の指摘・検証エラーがあれば失敗（終了コード 1）。</summary>
    public static bool Fails(AnalysisReport r, FailOnLevel lvl) => lvl switch
    {
        FailOnLevel.None => false,
        FailOnLevel.Info => r.Info + r.Warning + r.Critical + r.Errors > 0,
        FailOnLevel.Warning => r.Warning + r.Critical + r.Errors > 0,
        FailOnLevel.Critical => r.Critical + r.Errors > 0,
        _ => false,
    };
}
