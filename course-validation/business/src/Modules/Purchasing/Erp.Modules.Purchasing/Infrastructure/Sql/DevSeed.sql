-- Development and test data; never runs in production.
INSERT INTO purchasing.supplier (name, tin, address, email, phone, payment_terms_days, is_active)
SELECT v.name, v.tin, v.address, v.email, v.phone, v.terms, TRUE
FROM (VALUES ('Manila Hardware Supply', '567-890-123-000', 'Binondo, Manila', 'orders@manilahardware.test', '02 8123 4567', 30),
             ('Cebu Metals Inc.',       '678-901-234-000', 'Mandaue City',    'sales@cebumetals.test',     '032 234 5678', 45))
     AS v(name, tin, address, email, phone, terms)
WHERE NOT EXISTS (SELECT 1 FROM purchasing.supplier);
