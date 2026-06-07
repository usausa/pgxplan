using System;
using System.Collections.Generic;
using System.Text;

namespace PgPlanCheck;

/// <summary>
/// SQL テキストを文単位に分割するユーティリティ。
/// 文字列リテラル('...')、引用識別子("..."), 行コメント(--), ブロックコメント(/* */),
/// ドル引用符($tag$ ... $tag$) の内側にあるセミコロンは区切りとして扱わない。
/// </summary>
public static class SqlSplitter
{
    public static List<string> Split(string sql)
    {
        var statements = new List<string>();
        var sb = new StringBuilder();
        int i = 0, n = sql.Length;

        while (i < n)
        {
            char c = sql[i];

            // 行コメント
            if (c == '-' && i + 1 < n && sql[i + 1] == '-')
            {
                while (i < n && sql[i] != '\n') sb.Append(sql[i++]);
                continue;
            }
            // ブロックコメント
            if (c == '/' && i + 1 < n && sql[i + 1] == '*')
            {
                sb.Append("/*"); i += 2;
                while (i < n && !(sql[i] == '*' && i + 1 < n && sql[i + 1] == '/')) sb.Append(sql[i++]);
                if (i < n) { sb.Append("*/"); i += 2; }
                continue;
            }
            // 文字列リテラル / 引用識別子（'' "" によるエスケープに対応）
            if (c == '\'' || c == '"')
            {
                char q = c;
                sb.Append(sql[i++]);
                while (i < n)
                {
                    sb.Append(sql[i]);
                    if (sql[i] == q)
                    {
                        if (i + 1 < n && sql[i + 1] == q) { sb.Append(sql[i + 1]); i += 2; continue; }
                        i++; break;
                    }
                    i++;
                }
                continue;
            }
            // ドル引用符
            if (c == '$')
            {
                int j = i + 1;
                while (j < n && (char.IsLetterOrDigit(sql[j]) || sql[j] == '_')) j++;
                if (j < n && sql[j] == '$')
                {
                    string tag = sql.Substring(i, j - i + 1);
                    int end = sql.IndexOf(tag, j + 1, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        sb.Append(sql.Substring(i)); i = n;
                    }
                    else
                    {
                        sb.Append(sql.Substring(i, end + tag.Length - i)); i = end + tag.Length;
                    }
                    continue;
                }
            }
            // 文の区切り
            if (c == ';')
            {
                AddIfNotEmpty(statements, sb);
                sb.Clear(); i++;
                continue;
            }

            sb.Append(c); i++;
        }

        AddIfNotEmpty(statements, sb);
        return statements;
    }

    private static void AddIfNotEmpty(List<string> list, StringBuilder sb)
    {
        var s = sb.ToString().Trim();
        if (s.Length > 0) list.Add(s);
    }

    private static readonly string[] Explainable =
    {
        "select", "insert", "update", "delete", "with", "values", "table", "merge"
    };

    /// <summary>EXPLAIN 可能な文か（先頭コメントを除いた最初のキーワードで判定）。</summary>
    public static bool IsExplainable(string statement)
    {
        var kw = FirstKeyword(statement);
        return Array.Exists(Explainable, k => k == kw);
    }

    /// <summary>先頭のコメント・空白を読み飛ばし、最初の単語を小文字で返す。</summary>
    public static string FirstKeyword(string statement)
    {
        int i = 0, n = statement.Length;
        while (i < n)
        {
            char c = statement[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '-' && i + 1 < n && statement[i + 1] == '-')
            {
                while (i < n && statement[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < n && statement[i + 1] == '*')
            {
                i += 2;
                while (i < n && !(statement[i] == '*' && i + 1 < n && statement[i + 1] == '/')) i++;
                i += 2;
                continue;
            }
            break;
        }
        int start = i;
        while (i < n && (char.IsLetter(statement[i]))) i++;
        return statement.Substring(start, i - start).ToLowerInvariant();
    }
}
