-- Development/test data only; never runs in production. Designed to exercise every business rule.
-- Runs on a fresh database, so the identity columns hand out 1, 2, 3 in insert order.

INSERT INTO sales.customer (name, is_blocked, credit_limit, outstanding_balance)
SELECT v.name, v.is_blocked, v.credit_limit, v.outstanding_balance
FROM (VALUES ('Acme Corp',       FALSE, 50000, 10000),   -- healthy: ₱40,000 of headroom
             ('Beta Industries', FALSE,  5000,  4500),   -- almost maxed: only ₱500 left
             ('Gamma Traders',   TRUE,  20000,     0))   -- BLOCKED
     AS v(name, is_blocked, credit_limit, outstanding_balance)
WHERE NOT EXISTS (SELECT 1 FROM sales.customer);

INSERT INTO sales.item_category (name, max_quantity_per_order, required_role)
SELECT v.name, v.max_quantity_per_order, v.required_role
FROM (VALUES ('General',    1000, NULL),             -- anyone, high cap
             ('Controlled',    5, 'SalesManager'))   -- managers only, at most 5 per order
     AS v(name, max_quantity_per_order, required_role)
WHERE NOT EXISTS (SELECT 1 FROM sales.item_category);

INSERT INTO sales.item (sku, name, category_id, unit_price, is_active)
SELECT v.sku, v.name, v.category_id, v.unit_price, TRUE
FROM (VALUES ('GEN-001', 'Widget',         1,  25.00),
             ('GEN-002', 'Gadget',         1,  40.00),
             ('CTL-001', 'Restricted Kit', 2, 500.00))
     AS v(sku, name, category_id, unit_price)
WHERE NOT EXISTS (SELECT 1 FROM sales.item);
