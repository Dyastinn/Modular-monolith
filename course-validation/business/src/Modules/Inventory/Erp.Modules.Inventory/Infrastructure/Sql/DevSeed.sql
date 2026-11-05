-- Development/test data only; never runs in production. Item 3 (Restricted Kit) has thin stock so the
-- reservation race is testable. Item ids match the Sales dev seed.
INSERT INTO inventory.stock_item (item_id, on_hand, reserved)
SELECT v.item_id, v.on_hand, v.reserved
FROM (VALUES (1, 100, 0),
             (2,  50, 0),
             (3,   8, 0)) AS v(item_id, on_hand, reserved)
WHERE NOT EXISTS (SELECT 1 FROM inventory.stock_item);
