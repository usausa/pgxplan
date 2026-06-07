using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using PgPlanCheck.Rules;

namespace PgPlanCheck.Reporting;

/// <summary>解析結果を SARIF 2.1.0 で出力する（D1。GitHub code scanning 等で利用可能）。</summary>
public sealed class SarifReportWriter : IReportWriter
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public void Write(AnalysisReport r, TextWriter output)
    {
        var results = new List<object>();

        foreach (var (src, _, f) in r.AllFindings())
        {
            results.Add(new
            {
                ruleId = f.RuleId ?? "unknown",
                level = SeverityText.Sarif(f.Severity),
                message = new { text = MessageText(f) },
                locations = new[]
                {
                    new { physicalLocation = new
                    {
                        artifactLocation = new { uri = Uri(src.Name) },
                        region = new { startLine = 1 },
                    } },
                },
                partialFingerprints = new Dictionary<string, string>
                {
                    ["pgPlanCheck/v1"] = Fingerprint.Of(src.Name, f),
                },
            });
        }

        foreach (var src in r.Sources)
            foreach (var st in src.Statements.Where(s => s.Error is not null))
                results.Add(new
                {
                    ruleId = "explain-error",
                    level = "error",
                    message = new { text = $"EXPLAIN 検証エラー (#{st.Index}): {st.Error}" },
                    locations = new[]
                    {
                        new { physicalLocation = new
                        {
                            artifactLocation = new { uri = Uri(src.Name) },
                            region = new { startLine = 1 },
                        } },
                    },
                });

        var root = new Dictionary<string, object?>
        {
            ["$schema"] = "https://json.schemastore.org/sarif-2.1.0.json",
            ["version"] = "2.1.0",
            ["runs"] = new[]
            {
                new
                {
                    tool = new { driver = new
                    {
                        name = r.Metadata.Tool,
                        version = r.Metadata.Version,
                        informationUri = "https://github.com/pgplancheck",
                        rules = RuleRegistry.Default.Select(rule => new { id = rule.Id }).ToArray(),
                    } },
                    results = results.ToArray(),
                },
            },
        };

        output.Write(JsonSerializer.Serialize(root, Opts));
        output.WriteLine();
    }

    private static string MessageText(Finding f)
    {
        var s = $"[{SeverityText.Ja(f.Severity)}] {f.Title} @ {f.NodeLabel}";
        if (f.Suggestion is not null) s += $"\n{f.Suggestion}";
        if (f.SuggestedSql is not null) s += $"\n{f.SuggestedSql}";
        return s;
    }

    private static string Uri(string name) => name.Replace('\\', '/');
}
