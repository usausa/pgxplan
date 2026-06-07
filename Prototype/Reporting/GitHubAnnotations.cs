using System.IO;
using System.Linq;

namespace PgPlanCheck.Reporting;

/// <summary>
/// GitHub Actions のワークフローコマンド（::warning::/::error::/::notice::）を出力する（D2）。
/// PR 上にインラインのアノテーションとして表示される。
/// </summary>
public static class GitHubAnnotations
{
    public static void Emit(AnalysisReport report, TextWriter output)
    {
        foreach (var (src, _, f) in report.AllFindings())
        {
            var cmd = SeverityText.GitHub(f.Severity);
            var title = EscapeProp($"[{SeverityText.Ja(f.Severity)}] {f.Title}");
            var file = EscapeProp(src.Name);
            var message = EscapeData(BuildMessage(f));
            output.WriteLine($"::{cmd} file={file},title={title}::{message}");
        }

        foreach (var src in report.Sources)
            foreach (var st in src.Statements.Where(s => s.Error is not null))
                output.WriteLine($"::error file={EscapeProp(src.Name)},title=EXPLAIN 検証エラー::{EscapeData($"#{st.Index}: {st.Error}")}");
    }

    private static string BuildMessage(Finding f)
    {
        var s = $"{f.NodeLabel}";
        if (f.Suggestion is not null) s += $"\n{f.Suggestion}";
        if (f.SuggestedSql is not null) s += $"\n{f.SuggestedSql}";
        return s;
    }

    // GitHub のワークフローコマンドのエスケープ規則。
    private static string EscapeData(string s)
        => s.Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A");

    private static string EscapeProp(string s)
        => s.Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A")
            .Replace(":", "%3A").Replace(",", "%2C");
}
