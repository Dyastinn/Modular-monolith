-- Development/test data only; never runs in production. All three users share the password 'Password123!'.
-- The hash is a real salted PBKDF2-HMAC-SHA256 value in the {iterations}.{salt}.{key} format PasswordHasher
-- produces, so login works out of the box.
INSERT INTO identity.app_user (username, password_hash, role, is_active, security_stamp, password_expires_utc)
SELECT v.username, v.password_hash, v.role, TRUE, gen_random_uuid(), now() + interval '90 days'
FROM (VALUES
  ('rep',     '600000.2KCoQTqX/lrPlY4QVlVPXg==.5yaeUmLG5LqYE9QBTspiKqER/NSvNhE7mzYQno9q3tE=', 'SalesRep'),
  ('manager', '600000.2KCoQTqX/lrPlY4QVlVPXg==.5yaeUmLG5LqYE9QBTspiKqER/NSvNhE7mzYQno9q3tE=', 'SalesManager'),
  ('admin',   '600000.2KCoQTqX/lrPlY4QVlVPXg==.5yaeUmLG5LqYE9QBTspiKqER/NSvNhE7mzYQno9q3tE=', 'Admin')
) AS v(username, password_hash, role)
WHERE NOT EXISTS (SELECT 1 FROM identity.app_user);
