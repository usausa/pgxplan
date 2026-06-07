-- status='pending'（全体の約2%）で絞り込み。
-- status にインデックスが無いため全件走査(Seq Scan)になり、98%の行がフィルタで捨てられる。
SELECT id, customer_id, amount, created_at
FROM explain_demo.orders
WHERE status = 'pending'
ORDER BY created_at DESC
LIMIT 50;
