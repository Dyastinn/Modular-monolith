-- Development only, and safe to run again: fills in the blank company profile with a demo company.
UPDATE invoicing.company_profile
SET registered_name = 'Demo Trading Corp.', trade_name = 'Demo Hardware', tin = '000-111-222-00000',
    registered_address = 'Ortigas Center, Pasig City'
WHERE id = 1 AND registered_name = '';
