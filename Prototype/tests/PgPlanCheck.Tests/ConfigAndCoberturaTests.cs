using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using PgPlanCheck.Config;
using PgPlanCheck.Reporting;
using PgPlanCheck.Rules;
using Xunit;

namespace PgPlanCheck.Tests;

/// <summary>Cobertura 出力（D1 拡張）とルール設定ファイルのテスト。</summary>
public class ConfigAndCoberturaTests
{
    [Fact]
    public void Cobertura_maps_clean_statements_to_covered()
    {
        var sw = new StringWriter();
        new CoberturaReportWriter().Write(SampleReport.Mixed(), sw);
        var cov = XDocument.Parse(sw.ToString()).Root!;

        Assert.Equal("coverage", cov.Name.LocalName);
        Assert.Equal("3", cov.Attribute("lines-valid")!.Value);   // 文は計 3
        Assert.Equal("1", cov.Attribute("lines-covered")!.Value);  // スキップ文のみ健全

        var src1 = cov.Descendants("class").First(c => c.Attribute("filename")!.Value == "samples/01.sql");
        Assert.Equal("0", src1.Descendants("line").First(l => l.Attribute("number")!.Value == "1").Attribute("hits")!.Value); // 警告あり
        Assert.Equal("1", src1.Descendants("line").First(l => l.Attribute("number")!.Value == "2").Attribute("hits")!.Value); // スキップ
    }

    [Fact]
    public void Cobertura_line_rate_is_invariant_culture()
    {
        var sw = new StringWriter();
        new CoberturaReportWriter().Write(SampleReport.Mixed(), sw);
        var cov = XDocument.Parse(sw.ToString()).Root!;
        Assert.Equal("0.3333", cov.Attribute("line-rate")!.Value); // 1/3、小数点はドット
    }

    [Fact]
    public void Resolve_whitelist_keeps_only_enabled_rules()
    {
        var rules = RuleRegistry.Resolve(new[] { "seq-scan", "external-sort" }, null, out var unknown);
        Assert.Equal(2, rules.Count);
        Assert.All(rules, r => Assert.Contains(r.Id, new[] { "seq-scan", "external-sort" }));
        Assert.Empty(unknown);
    }

    [Fact]
    public void Resolve_blacklist_removes_disabled_rules()
    {
        int all = RuleRegistry.Default.Count;
        var rules = RuleRegistry.Resolve(null, new[] { "jit-overhead" }, out _);
        Assert.Equal(all - 1, rules.Count);
        Assert.DoesNotContain(rules, r => r.Id == "jit-overhead");
    }

    [Fact]
    public void Resolve_reports_unknown_ids()
    {
        RuleRegistry.Resolve(new[] { "seq-scan", "nope" }, null, out var unknown);
        Assert.Contains("nope", unknown);
    }

    [Fact]
    public void No_config_uses_all_rules()
    {
        var rules = RuleRegistry.Resolve(null, null, out _);
        Assert.Equal(RuleRegistry.Default.Count, rules.Count);
    }

    [Fact]
    public async Task Config_file_round_trips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pgpc_cfg_{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, """{ "rules": { "disabled": ["jit-overhead", "trigger-time"] } }""");
        try
        {
            var cfg = await ConfigLoader.LoadAsync(path);
            Assert.Contains("jit-overhead", cfg.Rules.Disabled);
            var rules = RuleRegistry.Resolve(cfg.Rules.Enabled, cfg.Rules.Disabled, out _);
            Assert.DoesNotContain(rules, r => r.Id == "trigger-time");
        }
        finally { File.Delete(path); }
    }
}
