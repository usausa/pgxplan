using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using PgPlanCheck.Catalog;
using PgPlanCheck.Input;
using PgPlanCheck.Reporting;
using PgPlanCheck.Rules;

namespace PgPlanCheck;

/// <summary>
/// 入力一覧を順に EXPLAIN・解析し、構造化結果（<see cref="AnalysisReport"/>）を組み立てる。
/// <paramref name="live"/> が true（コンソール形式）のときは従来どおり色付きで逐次出力し、
/// What-if 試算も行う。構造化形式では結果モデルのみを構築する。
/// </summary>
public static class AnalysisEngine
{
    private static readonly Regex ParamPlaceholder = new(@"\$\d+", RegexOptions.Compiled);

    public static async Task<AnalysisReport> RunAsync(
        NpgsqlConnection conn,
        IReadOnlyList<SqlSource> sources,
        AppOptions options,
        IReadOnlyList<IPlanRule> rules,
        HashSet<string>? baseline,
        bool live,
        CancellationToken ct = default)
    {
        var report = new AnalysisReport { Metadata = new RunMetadata { Analyzed = options.Analyze } };
        var runner = new PgExplainRunner(conn);
        var analyzer = new PlanAnalyzer(rules);

        bool hypoPg = options.WhatIf && await runner.HasHypoPgAsync(ct); // B3: 利用可能なら仮想索引で What-if

        foreach (var source in sources)
        {
            var sr = new SourceResult { Name = source.Name };
            report.Sources.Add(sr);

            var statements = SqlSplitter.Split(source.Sql);
            if (live) ReportPrinter.PrintSourceHeader(source.Name, statements.Count);

            for (int idx = 0; idx < statements.Count; idx++)
            {
                var stmt = statements[idx];
                var no = idx + 1;

                if (!SqlSplitter.IsExplainable(stmt))
                {
                    var kw = SqlSplitter.FirstKeyword(stmt);
                    if (live) ReportPrinter.PrintSkipped(no, kw);
                    sr.Statements.Add(new StatementResult { Index = no, Sql = stmt, Skipped = true, SkipKeyword = kw });
                    continue;
                }

                if (live) ReportPrinter.PrintStatementHeader(no, stmt);

                // C2: パラメータ化 SQL（$1..）は --param で値をバインドする。未指定なら案内して飛ばす。
                bool parameterized = ParamPlaceholder.IsMatch(stmt);
                IReadOnlyList<string>? prms = null;
                if (parameterized)
                {
                    if (options.Params.Count > 0)
                    {
                        prms = options.Params;
                    }
                    else
                    {
                        const string msg = "パラメータ化 SQL（$1 等）です。--param <値> を $1, $2, … の順に指定してください（例: --param 12345）。";
                        if (live) ReportPrinter.PrintError(no, msg);
                        sr.Statements.Add(new StatementResult { Index = no, Sql = stmt, Error = msg });
                        continue;
                    }
                }

                ExplainRoot? root;
                try
                {
                    root = await runner.RunAsync(stmt, options.Analyze, prms, ct);
                }
                catch (PostgresException ex)
                {
                    var error = $"{ex.SqlState} {ex.MessageText}"
                        + (ex.Position > 0 ? $"（位置 {ex.Position}）" : "")
                        + (ex.Hint is not null ? $"\nヒント: {ex.Hint}" : "");
                    if (live) ReportPrinter.PrintError(no, error);
                    sr.Statements.Add(new StatementResult { Index = no, Sql = stmt, Error = error });
                    continue;
                }

                if (root is null)
                {
                    const string error = "プランを取得できませんでした。";
                    if (live) ReportPrinter.PrintError(no, error);
                    sr.Statements.Add(new StatementResult { Index = no, Sql = stmt, Error = error });
                    continue;
                }

                if (live) ReportPrinter.PrintPlanSummary(root, options.Analyze);

                var catalog = await LoadCatalogAsync(conn, root, ct);
                var all = analyzer.Analyze(root, options.Analyze, catalog);
                var findings = ApplyBaseline(all, source.Name, baseline, report);

                if (live) ReportPrinter.PrintFindings(findings);
                if (live && options.WhatIf && !parameterized)
                    await RunWhatIfAsync(runner, stmt, findings, root, options, hypoPg, ct);

                sr.Statements.Add(new StatementResult
                {
                    Index = no,
                    Sql = stmt,
                    PlanningTimeMs = root.PlanningTime,
                    ExecutionTimeMs = root.ExecutionTime,
                    Findings = findings,
                });
            }
        }

        return report;
    }

    private static async Task<ICatalog> LoadCatalogAsync(NpgsqlConnection conn, ExplainRoot root, CancellationToken ct)
    {
        // 既存インデックス・列統計（B1）。取得に失敗しても解析は継続する。
        try
        {
            var rels = PlanWalker.Walk(root.Plan)
                .Select(n => n.Node)
                .Where(n => n.RelationName is not null)
                .Select(n => (n.Schema, Table: n.RelationName!))
                .Distinct()
                .ToList();
            return await PgCatalogLoader.LoadAsync(conn, rels, ct);
        }
        catch (PostgresException)
        {
            return NullCatalog.Instance;
        }
    }

    private static List<Finding> ApplyBaseline(IReadOnlyList<Finding> findings, string source, HashSet<string>? baseline, AnalysisReport report)
    {
        if (baseline is null || baseline.Count == 0) return findings.ToList();

        var kept = new List<Finding>();
        foreach (var f in findings)
        {
            if (baseline.Contains(Fingerprint.Of(source, f))) report.SuppressedCount++;
            else kept.Add(f);
        }
        return kept;
    }

    private static async Task RunWhatIfAsync(
        PgExplainRunner runner, string stmt, IReadOnlyList<Finding> findings,
        ExplainRoot before, AppOptions options, bool hypoPg, CancellationToken ct)
    {
        var indexSqls = findings
            .Where(f => f.SuggestedSql is not null && f.SuggestedSql.StartsWith("CREATE INDEX", StringComparison.OrdinalIgnoreCase))
            .Select(f => f.SuggestedSql!)
            .Distinct()
            .ToList();
        if (indexSqls.Count == 0) return;

        try
        {
            if (hypoPg)
            {
                // B3: HypoPG の仮想索引で推定（実体を作らない・ゼロ書込）。実行はできないためコスト比較のみ。
                var after = await runner.RunWithHypotheticalAsync(stmt, indexSqls, ct);
                ReportPrinter.PrintWhatIf(before, after, indexSqls, analyzed: false, hypoPg: true);
            }
            else
            {
                var after = await runner.RunWithIndexesAsync(stmt, options.Analyze, indexSqls, ct);
                ReportPrinter.PrintWhatIf(before, after, indexSqls, options.Analyze, hypoPg: false);
            }
        }
        catch (PostgresException ex)
        {
            Console.Error.WriteLine($"   What-if 試算でエラー: {ex.SqlState} {ex.MessageText}");
        }
    }
}
