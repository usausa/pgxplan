using System;
using System.Collections.Generic;
using Npgsql;
using PgPlanCheck.Reporting;

namespace PgPlanCheck;

/// <summary>コマンドライン引数。接続先は確認環境(192.168.100.90 / test)を既定値とする。</summary>
public sealed class AppOptions
{
    public string Host { get; set; } = "192.168.100.90";
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = "test";
    public string Username { get; set; } = "test";
    public string Password { get; set; } = "test";

    // ---- 入力（C1）----
    /// <summary>位置引数（ファイル / ディレクトリ / glob / "-"=stdin）。</summary>
    public List<string> Inputs { get; } = new();
    /// <summary>--sql で直接指定した SQL（複数可）。</summary>
    public List<string> InlineSql { get; } = new();
    /// <summary>stdin から読み込むか（"-" または --stdin）。</summary>
    public bool UseStdin { get; set; }

    /// <summary>パラメータ化 SQL（$1..）にバインドする値（--param、複数可）（C2）。</summary>
    public List<string> Params { get; } = new();

    // ---- 出力（D1）----
    public OutputFormat Format { get; set; } = OutputFormat.Console;
    public string? OutputPath { get; set; }
    public bool NoColor { get; set; }

    // ---- CI ゲート（D2）----
    public FailOnLevel FailOn { get; set; } = FailOnLevel.Warning;
    public string? BaselinePath { get; set; }
    public string? WriteBaselinePath { get; set; }
    public bool GitHubAnnotations { get; set; }

    /// <summary>ワークロード統合インデックス推奨を SQL スクリプトに書き出す先（B4）。</summary>
    public string? AdviceSqlPath { get; set; }

    /// <summary>設定ファイル（JSON）。使用するルールの有効/無効を指定する。</summary>
    public string? ConfigPath { get; set; }

    /// <summary>true で EXPLAIN ANALYZE（実測・ただし ROLLBACK で更新は永続化しない）。既定 false=検証専用。</summary>
    public bool Analyze { get; set; }

    /// <summary>true で、提案された CREATE INDEX をトランザクション内で仮適用し改善効果を試算（ROLLBACK で破棄）。</summary>
    public bool WhatIf { get; set; }

    public bool InitDemo { get; set; }
    public bool ShowHelp { get; set; }

    /// <summary>解析対象の入力が 1 つでも指定されているか。</summary>
    public bool HasInput => Inputs.Count > 0 || InlineSql.Count > 0 || UseStdin;

    public static AppOptions Parse(string[] args, out string? error)
    {
        var o = new AppOptions();
        error = null;

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "-h":
                case "--help": o.ShowHelp = true; break;
                case "--analyze": o.Analyze = true; break;
                case "--what-if": o.WhatIf = true; break;
                case "--init-demo": o.InitDemo = true; break;
                case "--stdin": o.UseStdin = true; break;
                case "-": o.UseStdin = true; break;
                case "--no-color": o.NoColor = true; break;
                case "--github-annotations": o.GitHubAnnotations = true; break;

                case "--sql": o.InlineSql.Add(Next(args, ref i, a, ref error)); break;
                case "--param": o.Params.Add(Next(args, ref i, a, ref error)); break;
                case "--advice-sql": o.AdviceSqlPath = Next(args, ref i, a, ref error); break;
                case "--config": o.ConfigPath = Next(args, ref i, a, ref error); break;
                case "-o":
                case "--output": o.OutputPath = Next(args, ref i, a, ref error); break;
                case "--baseline": o.BaselinePath = Next(args, ref i, a, ref error); break;
                case "--write-baseline": o.WriteBaselinePath = Next(args, ref i, a, ref error); break;

                case "--format":
                    var fs = Next(args, ref i, a, ref error);
                    if (error is not null) break;
                    if (!FormatParsing.TryParseFormat(fs, out var fmt)) error = $"不明な出力形式: {fs}（console|json|markdown|sarif|junit）";
                    else o.Format = fmt;
                    break;
                case "--fail-on":
                    var ls = Next(args, ref i, a, ref error);
                    if (error is not null) break;
                    if (!FormatParsing.TryParseFailOn(ls, out var lvl)) error = $"不明な fail-on レベル: {ls}（none|info|warning|critical）";
                    else o.FailOn = lvl;
                    break;

                case "--host": o.Host = Next(args, ref i, a, ref error); break;
                case "--port":
                    var ps = Next(args, ref i, a, ref error);
                    if (error is null && !int.TryParse(ps, out var port)) { error = $"--port が数値ではありません: {ps}"; }
                    else o.Port = error is null ? int.Parse(ps) : o.Port;
                    break;
                case "--db":
                case "--database": o.Database = Next(args, ref i, a, ref error); break;
                case "--user":
                case "--username": o.Username = Next(args, ref i, a, ref error); break;
                case "--password": o.Password = Next(args, ref i, a, ref error); break;
                default:
                    if (a.StartsWith('-')) { error = $"不明なオプション: {a}"; }
                    else { o.Inputs.Add(a); }
                    break;
            }
            if (error is not null) break;
        }
        return o;
    }

    private static string Next(string[] args, ref int i, string flag, ref string? error)
    {
        if (i + 1 >= args.Length) { error = $"{flag} に値がありません"; return ""; }
        return args[++i];
    }

    public string BuildConnectionString() => new NpgsqlConnectionStringBuilder
    {
        Host = Host,
        Port = Port,
        Database = Database,
        Username = Username,
        Password = Password,
        Timeout = 15,
        CommandTimeout = 120,
        ApplicationName = "PgPlanCheck"
    }.ConnectionString;

    public static void PrintUsage()
    {
        Console.WriteLine(@"PgPlanCheck — PostgreSQL 実行プラン検証ツール（プロトタイプ）

使い方:
  PgPlanCheck <入力...> [オプション]          指定 SQL を EXPLAIN で検証し、警告と改善案を出力
  PgPlanCheck --init-demo [接続オプション]    検証用のテストテーブル/データとサンプル SQL を作成

入力（複数可・組み合わせ可）:
  <file.sql>         SQL ファイル
  <dir>              ディレクトリ（再帰的に *.sql を対象）
  <glob>             ワイルドカード（例: samples/*.sql）
  -, --stdin         標準入力から読み込む
  --sql ""<text>""     SQL を直接指定（複数指定可）
  --param <v>        パラメータ化 SQL の $1.. に値を割当（$1, $2, … の順に複数指定可）

モード:
  （既定）            プレーン EXPLAIN。クエリを実行せず構文・存在チェックと推定プランのみ。DB は一切変更しない。
  --analyze          EXPLAIN ANALYZE。実測値で精密に判定。実行はするが必ず ROLLBACK し更新は永続化しない。
  --what-if          提案された CREATE INDEX を一時作成して効果（コスト/時間）を試算（console 形式のみ）。

出力（D1）:
  --format <fmt>     console（既定）| json | markdown | sarif | junit | cobertura
  -o, --output <f>   構造化レポートの出力先ファイル（省略時は標準出力）
  --no-color         色付けを無効化
  --config <f>       設定ファイル（JSON）。使用するルールの有効/無効を指定

CI ゲート（D2）:
  --fail-on <lvl>    none | info | warning（既定）| critical … しきい値以上で終了コード 1
  --baseline <f>     既知問題（フィンガープリント JSON）を除外
  --write-baseline <f>  現在の指摘をベースラインとして書き出して終了
  --github-annotations  GitHub Actions 用の ::warning:: / ::error:: を出力
  --advice-sql <f>   全 SQL 横断の統合インデックス推奨を SQL スクリプトに書き出す（B4）

接続オプション（既定は確認環境）:
  --host <h>         ホスト           (既定: 192.168.100.90)
  --port <p>         ポート           (既定: 5432)
  --db <name>        データベース     (既定: test)
  --user <name>      ユーザ           (既定: test)
  --password <pw>    パスワード       (既定: test)
  -h, --help         このヘルプ

例:
  dotnet run -- --init-demo
  dotnet run -- samples/01_missing_index.sql
  dotnet run -- samples/ --analyze --format sarif -o report.sarif
  dotnet run -- samples/ --fail-on warning --github-annotations
  Get-Content q.sql | dotnet run -- - --format json");
    }
}
