-- 主キーによる単一行検索。インデックス(PK)が効くので警告は出ない想定（正常系の確認用）。
SELECT *
FROM explain_demo.orders
WHERE id = 12345;
