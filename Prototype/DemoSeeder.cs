using System;
using System.IO;
using System.Threading.Tasks;
using Npgsql;

namespace PgPlanCheck;

/// <summary>検証用のテストスキーマ・データ・サンプル SQL を作成する。</summary>
public static class DemoSeeder
{
    private const string SchemaScript = @"
DROP SCHEMA IF EXISTS explain_demo CASCADE;
CREATE SCHEMA explain_demo;

-- 顧客（5万件）
CREATE TABLE explain_demo.customers (
    id         int PRIMARY KEY,
    name       text NOT NULL,
    region     text NOT NULL,
    created_at timestamptz NOT NULL
);
INSERT INTO explain_demo.customers (id, name, region, created_at)
SELECT g,
       'customer_' || g,
       (ARRAY['north','south','east','west','central'])[(random()*4+1)::int],
       now() - (random()*1000 || ' days')::interval
FROM generate_series(1, 50000) AS g;

-- 注文（50万件）。status='pending' は約2%だけ、amount は 0〜10000 の一様分布。
-- いずれもインデックスを張らないので、絞り込み系クエリは全件走査になる。
CREATE TABLE explain_demo.orders (
    id          bigserial PRIMARY KEY,
    customer_id int NOT NULL,
    status      text NOT NULL,
    amount      numeric(10,2) NOT NULL,
    created_at  timestamptz NOT NULL,
    note        text
);
INSERT INTO explain_demo.orders (customer_id, status, amount, created_at, note)
SELECT (random()*49999+1)::int,
       CASE WHEN random() < 0.02 THEN 'pending'
            ELSE (ARRAY['new','paid','shipped','cancelled'])[(random()*3+1)::int] END,
       (random()*10000)::numeric(10,2),
       now() - (random()*365 || ' days')::interval,
       md5(random()::text) || md5(random()::text)
FROM generate_series(1, 500000) AS g;

ANALYZE explain_demo.customers;
ANALYZE explain_demo.orders;
";

    public static async Task SeedAsync(NpgsqlConnection conn)
    {
        Console.WriteLine("テストデータを作成中（注文 50 万件など。数十秒かかる場合があります）...");
        await using (var cmd = new NpgsqlCommand(SchemaScript, conn) { CommandTimeout = 300 })
        {
            await cmd.ExecuteNonQueryAsync();
        }
        Console.WriteLine("  スキーマ explain_demo を作成しました（customers: 5万件 / orders: 50万件）。");

        var dir = WriteSamples();
        Console.WriteLine($"  サンプル SQL を出力しました: {dir}");
    }

    private static string WriteSamples()
    {
        var dir = Path.Combine(Directory.GetCurrentDirectory(), "samples");
        Directory.CreateDirectory(dir);

        File.WriteAllText(Path.Combine(dir, "01_missing_index.sql"),
@"-- status='pending'（全体の約2%）で絞り込み。
-- status にインデックスが無いため全件走査(Seq Scan)になり、98%の行がフィルタで捨てられる。
SELECT id, customer_id, amount, created_at
FROM explain_demo.orders
WHERE status = 'pending'
ORDER BY created_at DESC
LIMIT 50;
");

        File.WriteAllText(Path.Combine(dir, "02_join_and_filter.sql"),
@"-- 高額注文(amount>9500, 約5%)を顧客地域別に集計。
-- orders を全件走査しつつハッシュ結合する。amount にインデックスが無い点が論点。
SELECT c.region,
       count(*)      AS order_count,
       avg(o.amount) AS avg_amount
FROM explain_demo.orders o
JOIN explain_demo.customers c ON c.id = o.customer_id
WHERE o.amount > 9500
GROUP BY c.region
ORDER BY order_count DESC;
");

        File.WriteAllText(Path.Combine(dir, "03_external_sort.sql"),
@"-- note 列(約64文字)で全50万件をソート。
-- work_mem を超えるとディスクへの外部ソートになる（--analyze 指定時に実検出）。
SELECT id, note
FROM explain_demo.orders
ORDER BY note;
");

        File.WriteAllText(Path.Combine(dir, "04_indexed_lookup.sql"),
@"-- 主キーによる単一行検索。インデックス(PK)が効くので警告は出ない想定（正常系の確認用）。
SELECT *
FROM explain_demo.orders
WHERE id = 12345;
");

        return dir;
    }
}
