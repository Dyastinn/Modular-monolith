-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- The teams list: a search, a sort from the allow-list (TeamsValidator), a page. Members are counted, not listed.
CREATE OR REPLACE FUNCTION identity.search_teams(p_search text, p_sort text, p_desc boolean, p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, name text, is_active boolean, notify_mode text, member_count bigint)
LANGUAGE sql STABLE
AS $$
    WITH t AS (
        SELECT t.id, t.name::text AS name, t.is_active, t.notify_mode::text AS notify_mode,
               (SELECT COUNT(*) FROM identity.team_member m WHERE m.team_id = t.id) AS member_count
        FROM identity.team t
        WHERE p_search IS NULL OR t.name ILIKE '%' || p_search || '%'
    )
    SELECT * FROM t
    ORDER BY
        CASE WHEN p_sort = 'name' AND p_desc THEN t.name END DESC,
        CASE WHEN p_sort = 'members' AND NOT p_desc THEN t.member_count END ASC,
        CASE WHEN p_sort = 'members' AND p_desc     THEN t.member_count END DESC,
        t.name, t.id
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;

CREATE OR REPLACE FUNCTION identity.count_teams(p_search text)
RETURNS bigint
LANGUAGE sql STABLE
AS $$
    SELECT COUNT(*) FROM identity.team t
    WHERE p_search IS NULL OR t.name ILIKE '%' || p_search || '%';
$$;
