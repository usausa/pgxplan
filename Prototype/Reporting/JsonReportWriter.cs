using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PgPlanCheck.Reporting;

/// <summary>解析結果を JSON で出力する（D1）。</summary>
public sealed class JsonReportWriter : IReportWriter
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 日本語をエスケープせず読みやすく
    };

    public void Write(AnalysisReport r, TextWriter output)
    {
        var dto = new
        {
            r.Metadata.Tool,
            r.Metadata.Version,
            analyzed = r.Metadata.Analyzed,
            timestamp = r.Metadata.Timestamp,
            summary = new
            {
                r.Critical,
                r.Warning,
                r.Info,
                errors = r.Errors,
                suppressed = r.SuppressedCount,
                sources = r.Sources.Count,
                statements = r.StatementCount,
            },
            sources = r.Sources.Select(s => new
            {
                s.Name,
                statements = s.Statements.Select(st => new
                {
                    st.Index,
                    st.Skipped,
                    skipKeyword = st.SkipKeyword,
                    st.Error,
                    planningTimeMs = st.PlanningTimeMs,
                    executionTimeMs = st.ExecutionTimeMs,
                    findings = st.Findings.Select(f => new
                    {
                        ruleId = f.RuleId,
                        severity = f.Severity.ToString(),
                        f.Title,
                        node = f.NodeLabel,
                        f.Details,
                        suggestion = f.Suggestion,
                        sql = f.SuggestedSql,
                        fingerprint = Fingerprint.Of(s.Name, f),
                    }),
                }),
            }),
            recommendations = WorkloadAdvisor.Recommend(r).Select(rec => new
            {
                rec.Table,
                columns = rec.Columns,
                createSql = rec.CreateSql,
                rec.Occurrences,
                rec.Sources,
            }),
        };

        output.Write(JsonSerializer.Serialize(dto, Opts));
        output.WriteLine();
    }
}
