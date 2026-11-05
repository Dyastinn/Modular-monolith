-- Development and test data; never runs in production. Item ids match the Sales dev seed.
-- Item 3 has thin stock so the reservation race is testable. Item 5 is sold by the kilogram.
INSERT INTO inventory.item (id, sku, name, unit, allows_fractions, is_active, on_hand, reserved)
SELECT v.id, v.sku, v.name, v.unit, v.allows_fractions, TRUE, v.on_hand, 0
FROM (VALUES (1, 'GEN-001', 'Widget',         'pc', FALSE, 100),
             (2, 'GEN-002', 'Gadget',         'pc', FALSE,  50),
             (3, 'CTL-001', 'Restricted Kit', 'pc', FALSE,   8),
             (4, 'GEN-003', 'Server Rack',    'pc', FALSE, 100),
             (5, 'BLK-001', 'Copper Wire',    'kg', TRUE,  250))
     AS v(id, sku, name, unit, allows_fractions, on_hand)
WHERE NOT EXISTS (SELECT 1 FROM inventory.item);

-- Explicit ids don't advance the sequence; move it past them.
SELECT setval('inventory.item_id_seq', GREATEST((SELECT MAX(id) FROM inventory.item), 1));
