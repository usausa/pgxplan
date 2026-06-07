-- 高額注文(amount>9500, 約5%)を顧客地域別に集計。
-- orders を全件走査しつつハッシュ結合する。amount にインデックスが無い点が論点。
SELECT c.region,
       count(*)      AS order_count,
       avg(o.amount) AS avg_amount
FROM explain_demo.orders o
JOIN explain_demo.customers c ON c.id = o.customer_id
WHERE o.amount > 9500
GROUP BY c.region
ORDER BY order_count DESC;
