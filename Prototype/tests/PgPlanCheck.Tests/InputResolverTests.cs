using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PgPlanCheck.Input;
using Xunit;

namespace PgPlanCheck.Tests;

/// <summary>C1: 複数入力（ファイル/ディレクトリ/glob/stdin/インライン）の解決テスト。</summary>
public class InputResolverTests
{
    private static readonly string[] None = System.Array.Empty<string>();

    [Fact]
    public async Task Inline_sql_becomes_source()
    {
        var (sources, errors) = await InputResolver.ResolveAsync(None, new[] { "SELECT 1" }, false);

        var s = Assert.Single(sources);
        Assert.Equal("(--sql)", s.Name);
        Assert.Equal("SELECT 1", s.Sql);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task Multiple_inline_sql_are_numbered()
    {
        var (sources, _) = await InputResolver.ResolveAsync(None, new[] { "SELECT 1", "SELECT 2" }, false);
        Assert.Equal(2, sources.Count);
        Assert.Equal("(--sql #1)", sources[0].Name);
        Assert.Equal("(--sql #2)", sources[1].Name);
    }

    [Fact]
    public async Task File_resolves_and_missing_is_reported()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"pgpc_{Guid.NewGuid():N}.sql");
        await File.WriteAllTextAsync(tmp, "SELECT 1;");
        try
        {
            var (sources, errors) = await InputResolver.ResolveAsync(new[] { tmp, "does-not-exist.sql" }, None, false);
            Assert.Single(sources);
            Assert.Contains(errors, e => e.Contains("見つかりません"));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public async Task Directory_recurses_sql_files_only()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pgpc_{Guid.NewGuid():N}");
        var sub = Path.Combine(dir, "sub");
        Directory.CreateDirectory(sub);
        await File.WriteAllTextAsync(Path.Combine(dir, "a.sql"), "SELECT 1;");
        await File.WriteAllTextAsync(Path.Combine(sub, "b.sql"), "SELECT 2;");
        await File.WriteAllTextAsync(Path.Combine(dir, "c.txt"), "ignore me");
        try
        {
            var (sources, _) = await InputResolver.ResolveAsync(new[] { dir }, None, false);
            Assert.Equal(2, sources.Count); // a.sql, b.sql のみ
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Glob_matches_pattern()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pgpc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "x1.sql"), "SELECT 1;");
        await File.WriteAllTextAsync(Path.Combine(dir, "x2.sql"), "SELECT 2;");
        await File.WriteAllTextAsync(Path.Combine(dir, "y.sql"), "SELECT 3;");
        try
        {
            var (sources, _) = await InputResolver.ResolveAsync(new[] { Path.Combine(dir, "x*.sql") }, None, false);
            Assert.Equal(2, sources.Count);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Stdin_is_read_from_reader()
    {
        var (sources, _) = await InputResolver.ResolveAsync(None, None, useStdin: true, stdin: new StringReader("SELECT 42;"));
        var s = Assert.Single(sources);
        Assert.Equal("(stdin)", s.Name);
        Assert.Contains("42", s.Sql);
    }
}
