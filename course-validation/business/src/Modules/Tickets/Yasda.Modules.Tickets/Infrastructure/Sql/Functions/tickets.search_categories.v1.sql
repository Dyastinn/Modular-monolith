-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- The categories list, and the choices on the "New ticket" form (p_active_only). The sort is one of the allow-listed names.
CREATE OR REPLACE FUNCTION tickets.search_categories(
    p_search text, p_active_only boolean, p_sort text, p_desc boolean, p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, name text, team_id bigint, team_name text, is_active boolean)
LANGUAGE sql STABLE
AS $$
    SELECT c.id, c.name::text, c.team_id, c.team_name::text, c.is_active
    FROM tickets.category c
    WHERE (p_search IS NULL OR c.name ILIKE '%' || p_search || '%')
      AND (NOT p_active_only OR c.is_active)
    ORDER BY
        CASE WHEN p_sort = 'name' AND p_desc THEN c.name END DESC,
        CASE WHEN p_sort = 'team' AND NOT p_desc THEN c.team_name END ASC NULLS LAST,
        CASE WHEN p_sort = 'team' AND p_desc     THEN c.team_name END DESC NULLS LAST,
        CASE WHEN p_sort = 'status' AND NOT p_desc THEN c.is_active END DESC,
        CASE WHEN p_sort = 'status' AND p_desc     THEN c.is_active END ASC,
        c.name, c.id
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;

CREATE OR REPLACE FUNCTION tickets.count_categories(p_search text, p_active_only boolean)
RETURNS bigint
LANGUAGE sql STABLE
AS $$
    SELECT COUNT(*) FROM tickets.category c
    WHERE (p_search IS NULL OR c.name ILIKE '%' || p_search || '%')
      AND (NOT p_active_only OR c.is_active);
$$;

-- One category, or no row when it doesn't exist.
CREATE OR REPLACE FUNCTION tickets.get_category(p_id bigint)
RETURNS TABLE (id bigint, name text, team_id bigint, team_name text, is_active boolean)
LANGUAGE sql STABLE
AS $$
    SELECT c.id, c.name::text, c.team_id, c.team_name::text, c.is_active
    FROM tickets.category c
    WHERE c.id = p_id;
$$;
