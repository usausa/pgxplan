using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PgPlanCheck.Reporting;

/// <summary>既知問題のベースライン（指摘フィンガープリントの集合）の読み書き（D2）。</summary>
public static class Baseline
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static async Task<HashSet<string>> LoadAsync(string path)
    {
        await using var fs = File.OpenRead(path);
        var list = await JsonSerializer.DeserializeAsync<List<string>>(fs) ?? new List<string>();
        return new HashSet<string>(list, StringComparer.Ordinal);
    }

    public static async Task WriteAsync(string path, AnalysisReport report)
    {
        var prints = report.AllFindings()
            .Select(x => Fingerprint.Of(x.Source.Name, x.Finding))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        await using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, prints, Pretty);
    }
}
