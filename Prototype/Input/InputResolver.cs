using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PgPlanCheck.Input;

/// <summary>
/// コマンドライン入力を <see cref="SqlSource"/> の一覧に展開する（C1）。
/// ファイル / ディレクトリ（再帰 *.sql）/ glob（*,?）/ stdin（-）/ インライン（--sql）に対応。
/// </summary>
public static class InputResolver
{
    public static async Task<(List<SqlSource> Sources, List<string> Errors)> ResolveAsync(
        IReadOnlyList<string> inputs,
        IReadOnlyList<string> inlineSql,
        bool useStdin,
        TextReader? stdin = null)
    {
        var sources = new List<SqlSource>();
        var errors = new List<string>();

        foreach (var input in inputs)
        {
            if (input == "-") continue; // stdin は useStdin で扱う
            try
            {
                if (Directory.Exists(input))
                {
                    var files = EnumerateSorted(input, "*.sql", SearchOption.AllDirectories);
                    if (files.Count == 0) errors.Add($"ディレクトリに *.sql がありません: {input}");
                    foreach (var fp in files) sources.Add(new SqlSource(Rel(fp), await File.ReadAllTextAsync(fp)));
                }
                else if (HasWildcard(input))
                {
                    var dir = Path.GetDirectoryName(input);
                    if (string.IsNullOrEmpty(dir)) dir = ".";
                    var pattern = Path.GetFileName(input);
                    if (!Directory.Exists(dir)) { errors.Add($"ディレクトリが見つかりません: {dir}"); continue; }
                    var files = EnumerateSorted(dir, pattern, SearchOption.TopDirectoryOnly);
                    if (files.Count == 0) errors.Add($"パターンに一致するファイルがありません: {input}");
                    foreach (var fp in files) sources.Add(new SqlSource(Rel(fp), await File.ReadAllTextAsync(fp)));
                }
                else if (File.Exists(input))
                {
                    sources.Add(new SqlSource(input, await File.ReadAllTextAsync(input)));
                }
                else
                {
                    errors.Add($"入力が見つかりません: {input}");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{input}: {ex.Message}");
            }
        }

        int n = 1;
        foreach (var sql in inlineSql)
            sources.Add(new SqlSource(inlineSql.Count > 1 ? $"(--sql #{n++})" : "(--sql)", sql));

        if (useStdin)
        {
            var text = await (stdin ?? Console.In).ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(text)) errors.Add("stdin から SQL を読み取れませんでした。");
            else sources.Add(new SqlSource("(stdin)", text));
        }

        return (sources, errors);
    }

    private static List<string> EnumerateSorted(string dir, string pattern, SearchOption option)
        => Directory.EnumerateFiles(dir, pattern, option)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool HasWildcard(string s) => s.IndexOfAny(new[] { '*', '?' }) >= 0;

    private static string Rel(string p)
    {
        try { return Path.GetRelativePath(Directory.GetCurrentDirectory(), p); }
        catch { return p; }
    }
}
