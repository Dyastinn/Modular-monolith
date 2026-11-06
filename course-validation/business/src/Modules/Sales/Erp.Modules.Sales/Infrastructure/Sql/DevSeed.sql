-- Development and test data; never runs in production. Ids are 1, 2, 3... in insert order on a fresh database.

INSERT INTO sales.customer (name, tin, address, payment_terms_days, is_blocked, credit_limit, outstanding_balance)
SELECT v.name, v.tin, v.address, v.terms, v.is_blocked, v.credit_limit, v.outstanding_balance
FROM (VALUES ('Acme Corp',       '123-456-789-00000', 'Makati City',  30, FALSE,   50000, 10000),   -- ₱40,000 headroom
             ('Beta Industries', '234-567-890-00000', 'Pasig City',   30, FALSE,    5000,  4500),   -- only ₱500 left
             ('Gamma Traders',   NULL,              'Cebu City',     0, TRUE,    20000,     0),   -- blocked
             ('Delta Holdings',  '345-678-901-00000', 'Davao City',   45, FALSE,  200000,     0),   -- two high-value orders, not three
             ('Epsilon Retail',  '456-789-012-00000', 'Quezon City',  60, FALSE, 5000000,     0))   -- large account for approvals
     AS v(name, tin, address, terms, is_blocked, credit_limit, outstanding_balance)
WHERE NOT EXISTS (SELECT 1 FROM sales.customer);

INSERT INTO sales.item_category (name, max_quantity_per_order, required_role)
SELECT v.name, v.max_quantity_per_order, v.required_role
FROM (VALUES ('General',    1000, NULL),
             ('Controlled',    5, 'SalesManager'))
     AS v(name, max_quantity_per_order, required_role)
WHERE NOT EXISTS (SELECT 1 FROM sales.item_category);

-- Same ids as the Inventory dev seed. In real use these rows arrive through ItemCatalogChanged.
INSERT INTO sales.item (id, sku, name, unit, allows_fractions, is_active, category_id, unit_price, is_for_sale, tax_type)
SELECT v.id, v.sku, v.name, v.unit, v.allows_fractions, TRUE, v.category_id, v.unit_price, TRUE, 0
FROM (VALUES (1, 'GEN-001', 'Widget',         'pc', FALSE, 1,    25.00),
             (2, 'GEN-002', 'Gadget',         'pc', FALSE, 1,    40.00),
             (3, 'CTL-001', 'Restricted Kit', 'pc', FALSE, 2,   500.00),
             (4, 'GEN-003', 'Server Rack',    'pc', FALSE, 1, 75000.00),   -- one rack is over the ₱50,000 threshold
             (5, 'BLK-001', 'Copper Wire',    'kg', TRUE,  1,   620.50))
     AS v(id, sku, name, unit, allows_fractions, category_id, unit_price)
WHERE NOT EXISTS (SELECT 1 FROM sales.item);
