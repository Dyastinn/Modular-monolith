-- Released scripts are never edited: change a function with a .v2 file and a new migration.
CREATE OR REPLACE FUNCTION identity.search_users(p_search text, p_sort text, p_desc boolean, p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, email text, full_name text, role text, is_disabled boolean, must_change_password boolean,
               locked_out_until timestamptz, last_sign_in timestamptz)
LANGUAGE sql STABLE
AS $$
    WITH u AS (
        SELECT u.id, u.email::text AS email, u.full_name::text AS full_name, COALESCE(r.name, '')::text AS role,
               u.is_disabled, u.must_change_password,
               CASE WHEN u.lockout_end > now() THEN u.lockout_end END AS locked_out_until,
               (SELECT MAX(a.occurred_utc) FROM identity.sign_in_attempt a WHERE a.user_id = u.id AND a.outcome = 'Succeeded') AS last_sign_in
        FROM identity.users u
        LEFT JOIN identity.user_roles ur ON ur.user_id = u.id
        LEFT JOIN identity.roles r ON r.id = ur.role_id
        WHERE p_search IS NULL OR u.email ILIKE '%' || p_search || '%' OR u.full_name ILIKE '%' || p_search || '%'
    )
    SELECT * FROM u
    ORDER BY
        CASE WHEN p_sort = 'email' AND NOT p_desc THEN u.email END ASC,
        CASE WHEN p_sort = 'email' AND p_desc     THEN u.email END DESC,
        CASE WHEN p_sort = 'role' AND NOT p_desc THEN u.role END ASC,
        CASE WHEN p_sort = 'role' AND p_desc     THEN u.role END DESC,
        CASE WHEN p_sort = 'lastSignIn' AND NOT p_desc THEN u.last_sign_in END ASC,
        CASE WHEN p_sort = 'lastSignIn' AND p_desc     THEN u.last_sign_in END DESC,
        CASE WHEN p_sort = 'fullName' AND p_desc THEN u.full_name END DESC,
        u.full_name, u.id
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;

CREATE OR REPLACE FUNCTION identity.count_users(p_search text)
RETURNS bigint
LANGUAGE sql STABLE
AS $$
    SELECT COUNT(*) FROM identity.users u
    WHERE p_search IS NULL OR u.email ILIKE '%' || p_search || '%' OR u.full_name ILIKE '%' || p_search || '%';
$$;

CREATE OR REPLACE FUNCTION identity.search_sign_in_attempts(p_email text, p_failed_only boolean, p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, email text, user_id bigint, outcome text, ip_address text, occurred_utc timestamptz)
LANGUAGE sql STABLE
AS $$
    SELECT a.id, a.email::text, a.user_id, a.outcome::text, a.ip_address::text, a.occurred_utc
    FROM identity.sign_in_attempt a
    WHERE (p_email IS NULL OR a.email ILIKE '%' || p_email || '%')
      AND (NOT p_failed_only OR a.outcome <> 'Succeeded')
    ORDER BY a.occurred_utc DESC, a.id DESC
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;

CREATE OR REPLACE FUNCTION identity.count_sign_in_attempts(p_email text, p_failed_only boolean)
RETURNS bigint
LANGUAGE sql STABLE
AS $$
    SELECT COUNT(*) FROM identity.sign_in_attempt a
    WHERE (p_email IS NULL OR a.email ILIKE '%' || p_email || '%')
      AND (NOT p_failed_only OR a.outcome <> 'Succeeded');
$$;
