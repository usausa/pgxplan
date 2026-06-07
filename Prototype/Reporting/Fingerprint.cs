using System.IO;

namespace PgPlanCheck.Reporting;

/// <summary>
/// 指摘の安定識別子。ベースライン（既知問題の除外）照合に使う。
/// 絶対パス差異を避けるためソースはファイル名のみを使い、ルール ID・ノード・タイトルで構成する。
/// </summary>
public static class Fingerprint
{
    public static string Of(string sourceName, Finding f)
        => $"{SafeName(sourceName)}|{f.RuleId}|{Norm(f.NodeLabel)}|{Norm(f.Title)}";

    private static string SafeName(string s)
    {
        try { return Path.GetFileName(s); }
        catch { return s; }
    }

    private static string Norm(string s) => (s ?? "").Replace('|', '/').Trim();
}
