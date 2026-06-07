using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Npgsql;
using PgPlanCheck;
using PgPlanCheck.Config;
using PgPlanCheck.Input;
using PgPlanCheck.Reporting;
using PgPlanCheck.Rules;

var options = AppOptions.Parse(args, out var parseError);

if (parseError is not null)
{
    Console.Error.WriteLine($"引数エラー: {parseError}");
    Console.Error.WriteLine("`--help` で使い方を表示します。");
    return 2;
}
if (options.ShowHelp || (!options.HasInput && !options.InitDemo))
{
    AppOptions.PrintUsage();
    return options.ShowHelp ? 0 : 2;
}

ReportPrinter.UseColor = !options.NoColor;
bool structured = options.Format != OutputFormat.Console;
// 構造化出力時は標準出力をレポート専用にし、人間向けメッセージは標準エラーへ回す。
TextWriter log = structured ? Console.Error : Console.Out;

try
{
    await using var conn = new NpgsqlConnection(options.BuildConnectionString());
    await conn.OpenAsync();
    log.WriteLine($"接続先: {options.Username}@{options.Host}:{options.Port}/{options.Database}");

    if (options.InitDemo)
    {
        await DemoSeeder.SeedAsync(conn);
        Console.WriteLine();
        Console.WriteLine("完了。次のように検証できます:");
        Console.WriteLine("  dotnet run -- samples/01_missing_index.sql");
        Console.WriteLine("  dotnet run -- samples/ --analyze --format markdown");
        return 0;
    }

    // 入力の解決（C1）
    var (sources, inputErrors) = await InputResolver.ResolveAsync(options.Inputs, options.InlineSql, options.UseStdin);
    foreach (var e in inputErrors) Console.Error.WriteLine($"入力警告: {e}");
    if (sources.Count == 0)
    {
        Console.Error.WriteLine("有効な入力がありません。");
        return 2;
    }

    log.WriteLine(options.Analyze
        ? "モード: --analyze（実測。ROLLBACK するため DB は更新されません）"
        : "モード: 検証専用（クエリは実行せず、推定プランのみ。DB は一切変更しません）");

    // ベースライン読込（書き出し時は適用しない）（D2）
    System.Collections.Generic.HashSet<string>? baseline = null;
    if (options.BaselinePath is not null && options.WriteBaselinePath is null)
    {
        try { baseline = await Baseline.LoadAsync(options.BaselinePath); }
        catch (Exception ex) { Console.Error.WriteLine($"ベースライン読込失敗: {ex.Message}"); }
    }

    // 使用ルールの選択（設定ファイル）。指定が無ければ全ルール。
    IReadOnlyList<IPlanRule> rules = RuleRegistry.Default;
    if (options.ConfigPath is not null)
    {
        try
        {
            var cfg = await ConfigLoader.LoadAsync(options.ConfigPath);
            rules = RuleRegistry.Resolve(cfg.Rules.Enabled, cfg.Rules.Disabled, out var unknownRules);
            foreach (var u in unknownRules) Console.Error.WriteLine($"設定警告: 未知のルール ID: {u}");
            log.WriteLine($"有効ルール: {rules.Count} / {RuleRegistry.AllIds.Count} 件");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"設定読込失敗: {ex.Message}");
        }
    }

    bool live = !structured;
    var report = await AnalysisEngine.RunAsync(conn, sources, options, rules, baseline, live);

    // ベースライン書き出し（D2）
    if (options.WriteBaselinePath is not null)
    {
        await Baseline.WriteAsync(options.WriteBaselinePath, report);
        Console.Error.WriteLine($"ベースラインを書き出しました: {options.WriteBaselinePath}（{report.TotalFindings} 件）");
        return 0;
    }

    // 出力（D1）
    if (structured)
    {
        var writer = ReportWriters.For(options.Format);
        if (options.OutputPath is not null)
        {
            await using var fw = new StreamWriter(options.OutputPath, append: false, new UTF8Encoding(false));
            writer.Write(report, fw);
            Console.Error.WriteLine($"レポートを書き出しました: {options.OutputPath}");
        }
        else
        {
            writer.Write(report, Console.Out);
        }
    }
    else
    {
        ReportPrinter.PrintRunSummary(report);
    }

    // ワークロード統合インデックス推奨（B4）
    var recommendations = WorkloadAdvisor.Recommend(report);
    if (!structured) ReportPrinter.PrintRecommendations(recommendations);
    if (options.AdviceSqlPath is not null)
    {
        await AdviceScript.WriteAsync(options.AdviceSqlPath, recommendations, report);
        Console.Error.WriteLine($"推奨インデックスのスクリプトを書き出しました: {options.AdviceSqlPath}（{recommendations.Count} 件）");
    }

    // GitHub アノテーション（D2）。構造化結果を標準出力へ流している場合は混在を避けて標準エラーへ。
    if (options.GitHubAnnotations)
    {
        TextWriter annOut = (structured && options.OutputPath is null) ? Console.Error : Console.Out;
        GitHubAnnotations.Emit(report, annOut);
    }

    // 終了コード（D2 fail-on）
    return CiGate.Fails(report, options.FailOn) ? 1 : 0;
}
catch (NpgsqlException ex)
{
    Console.Error.WriteLine($"接続/実行エラー: {ex.Message}");
    return 3;
}
