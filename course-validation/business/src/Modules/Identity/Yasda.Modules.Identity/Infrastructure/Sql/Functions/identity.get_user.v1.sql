-- Released scripts are never edited: change a function with a .v2 file and a new migration.
CREATE OR REPLACE FUNCTION identity.get_user(p_id bigint)
RETURNS TABLE (id bigint, email text, full_name text, role text, is_disabled boolean, must_change_password boolean,
               locked_out_until timestamptz, last_sign_in timestamptz)
LANGUAGE sql STABLE
AS $$
    SELECT u.id, u.email::text, u.full_name::text, COALESCE(r.name, '')::text,
           u.is_disabled, u.must_change_password,
           CASE WHEN u.lockout_end > now() THEN u.lockout_end END,
           (SELECT MAX(a.occurred_utc) FROM identity.sign_in_attempt a WHERE a.user_id = u.id AND a.outcome = 'Succeeded')
    FROM identity.users u
    LEFT JOIN identity.user_roles ur ON ur.user_id = u.id
    LEFT JOIN identity.roles r ON r.id = ur.role_id
    WHERE u.id = p_id;
$$;
