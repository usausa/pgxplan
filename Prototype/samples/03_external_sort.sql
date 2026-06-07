-- note 列(約64文字)で全50万件をソート。
-- work_mem を超えるとディスクへの外部ソートになる（--analyze 指定時に実検出）。
SELECT id, note
FROM explain_demo.orders
ORDER BY note;
