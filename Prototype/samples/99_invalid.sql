-- 存在しない列 customrr_id を参照（タイプミスを想定）。
-- 検証専用モードでも EXPLAIN がプラン作成を試みるため、列の誤りが検出される。
SELECT id, customrr_id
FROM explain_demo.orders
WHERE status = 'pending';
