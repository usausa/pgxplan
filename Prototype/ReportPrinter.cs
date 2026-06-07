using System;
using System.Collections.Generic;
using PgPlanCheck.Reporting;

namespace PgPlanCheck;

/// <summary>解析結果をコンソールへ整形出力する（既定の console 形式・色付き）。</summary>
public static class ReportPrinter
{
    /// <summary>色付き出力の有効/無効（--no-color で false に）。</summary>
    public static bool UseColor { get; set; } = true;

    public static void PrintSourceHeader(string name, int statementCount)
    {
        Console.WriteLine();
        WriteLine("════════════════════════════════════════════════════════════════", ConsoleColor.DarkGray);
        WriteLine($" 入力: {name}（{statementCount} 文）", ConsoleColor.White);
    }

    public static void PrintRunSummary(AnalysisReport report)
    {
        Console.WriteLine();
        Console.WriteLine("════════════════════════════════════════════════════════════════");
        var line = $" 集計: 重大 {report.Critical} / 警告 {report.Warning} / 情報 {report.Info}";
        if (report.Errors > 0) line += $" / 検証エラー {report.Errors}";
        if (report.SuppressedCount > 0) line += $" / 抑止 {report.SuppressedCount}";
        Console.WriteLine(line);
        Console.WriteLine("════════════════════════════════════════════════════════════════");
    }

    public static void PrintRecommendations(IReadOnlyList<IndexRecommendation> recs)
    {
        if (recs.Count == 0) return;
        Console.WriteLine();
        WriteLine(" ■ 推奨インデックス（ワークロード統合）", ConsoleColor.Cyan);
        foreach (var r in recs)
        {
            Console.Write($"   ・{r.Occurrences} 文で有効  ");
            WriteLine(FirstLine(r.CreateSql), ConsoleColor.DarkGreen);
            Console.WriteLine($"       対象: {string.Join(", ", r.Sources)}");
        }
    }

    public static void PrintStatementHeader(int index, string sql)
    {
        Console.WriteLine();
        WriteLine($"════════════════════════════════════════════════════════════════", ConsoleColor.DarkGray);
        WriteLine($" ステートメント #{index}", ConsoleColor.White);
        WriteLine($"────────────────────────────────────────────────────────────────", ConsoleColor.DarkGray);
        Console.WriteLine(Indent(Truncate(sql, 600), "   "));
    }

    public static void PrintSkipped(int index, string keyword)
    {
        Console.WriteLine();
        WriteLine($" ステートメント #{index}: EXPLAIN 対象外（{keyword.ToUpperInvariant()}）のためスキップしました。", ConsoleColor.DarkGray);
    }

    public static void PrintError(int index, string message)
    {
        Console.WriteLine();
        WriteLine($" ステートメント #{index}: EXPLAIN 実行エラー", ConsoleColor.Red);
        Console.WriteLine(Indent(message, "   "));
    }

    public static void PrintPlanSummary(ExplainRoot root, bool analyzed)
    {
        Console.WriteLine();
        WriteLine(" ■ 実行プラン", ConsoleColor.Cyan);
        if (analyzed)
        {
            if (root.PlanningTime is { } p) Console.WriteLine($"   Planning Time : {p:N3} ms");
            if (root.ExecutionTime is { } e) Console.WriteLine($"   Execution Time: {e:N3} ms");
        }
        else
        {
            Console.WriteLine("   （--no-analyze: 推定値のみ。実測時間・実測行数はありません）");
        }
        Console.WriteLine();
        PrintTree(root.Plan, 0, analyzed);
    }

    private static void PrintTree(PlanNode node, int depth, bool analyzed)
    {
        var indent = new string(' ', depth * 3);
        var prefix = depth == 0 ? "" : "-> ";
        var line = $"   {indent}{prefix}{node.Label}";

        string metrics;
        if (analyzed && node.ActualRows is { } act)
        {
            var loops = node.Loops > 1 ? $" x{node.Loops:N0}" : "";
            metrics = $"(推定 {node.PlanRows:N0} / 実測 {act:N0} 行, {node.ActualTotalTime:N1} ms{loops})";
        }
        else
        {
            metrics = $"(推定 {node.PlanRows:N0} 行, cost {node.TotalCost:N0})";
        }

        Console.Write(line + " ");
        WriteLine(metrics, ConsoleColor.DarkGray);

        if (node.Plans is null) return;
        foreach (var child in node.Plans)
            PrintTree(child, depth + 1, analyzed);
    }

    public static void PrintFindings(IReadOnlyList<Finding> findings)
    {
        Console.WriteLine();
        if (findings.Count == 0)
        {
            WriteLine(" ■ 検出された問題: なし — 明らかな性能上の警告は見つかりませんでした。", ConsoleColor.Green);
            return;
        }

        WriteLine($" ■ 検出された問題: {findings.Count} 件", ConsoleColor.Yellow);

        int i = 1;
        foreach (var f in findings)
        {
            var (tag, color) = f.Severity switch
            {
                Severity.Critical => ("重大", ConsoleColor.Red),
                Severity.Warning => ("警告", ConsoleColor.Yellow),
                _ => ("情報", ConsoleColor.Cyan),
            };

            Console.WriteLine();
            Console.Write($"  {i}. ");
            Write($"[{tag}] ", color);
            WriteLine(f.Title, color);
            Console.WriteLine($"      ノード: {f.NodeLabel}");
            foreach (var d in f.Details)
                Console.WriteLine($"        - {d}");

            if (f.Suggestion is not null)
            {
                Console.Write("      ");
                Write("→ 改善案: ", ConsoleColor.Green);
                Console.WriteLine(f.Suggestion.Replace("\n", Environment.NewLine + "                "));
            }
            if (f.SuggestedSql is not null)
            {
                WriteLine(Indent(f.SuggestedSql, "          "), ConsoleColor.DarkGreen);
            }
            i++;
        }
    }

    public static void PrintWhatIf(ExplainRoot before, ExplainRoot? after, IReadOnlyList<string> indexes, bool analyzed, bool hypoPg = false)
    {
        Console.WriteLine();
        WriteLine(hypoPg
            ? " ■ What-if: HypoPG 仮想インデックスで試算（実体を作成せず・ゼロ書込。推定コストのみ）"
            : " ■ What-if: 提案インデックスを仮適用（トランザクション内で作成→ROLLBACK。実 DB は変更されません）",
            ConsoleColor.Magenta);
        foreach (var ix in indexes)
            Console.WriteLine($"      適用: {FirstLine(ix)}");

        if (after is null)
        {
            Console.WriteLine("      （試算プランを取得できませんでした）");
            return;
        }

        double cb = before.Plan.TotalCost, ca = after.Plan.TotalCost;
        Console.Write($"      総コスト  : {cb:N0} → {ca:N0}   ");
        WriteDelta(cb, ca);

        if (analyzed && before.ExecutionTime is { } eb && after.ExecutionTime is { } ea)
        {
            Console.Write($"      実行時間  : {eb:N1} ms → {ea:N1} ms   ");
            WriteDelta(eb, ea);
        }

        Console.WriteLine($"      新しい上位ノード: {after.Plan.Label}");
    }

    private static void WriteDelta(double before, double after)
    {
        if (before <= 0) { Console.WriteLine(); return; }
        double change = (before - after) / before; // 正なら改善
        if (after < before)
            WriteLine($"▼ {change:P0} 改善", ConsoleColor.Green);
        else if (after > before)
            WriteLine($"▲ {-change:P0} 悪化", ConsoleColor.Red);
        else
            WriteLine("変化なし", ConsoleColor.DarkGray);
    }

    // ---------- 出力ユーティリティ ----------

    private static void Write(string s, ConsoleColor c)
    {
        if (!UseColor) { Console.Write(s); return; }
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = c;
        Console.Write(s);
        Console.ForegroundColor = prev;
    }

    private static void WriteLine(string s, ConsoleColor c)
    {
        if (!UseColor) { Console.WriteLine(s); return; }
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = c;
        Console.WriteLine(s);
        Console.ForegroundColor = prev;
    }

    private static string Indent(string text, string pad)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++) lines[i] = pad + lines[i];
        return string.Join(Environment.NewLine, lines);
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s.Substring(0, max) + " …(以下省略)";

    private static string FirstLine(string s)
    {
        int i = s.IndexOf('\n');
        return i < 0 ? s : s.Substring(0, i);
    }
}
