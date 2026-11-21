-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- The staff list of articles: the working copies, whatever their status. p_mine is a user id (or null for everyone's): the
-- articles that person wrote or may edit. The sort is one of the allow-listed names; without one, the newest change first.
CREATE OR REPLACE FUNCTION knowledge.search_articles(
    p_search text, p_status text, p_visibility text, p_mine bigint,
    p_sort text, p_desc boolean, p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, title text, summary text, status text, visibility text, current_version integer,
               created_by_id bigint, created_by_name text, last_edited_by_name text, updated_utc timestamptz)
LANGUAGE sql STABLE
AS $$
    SELECT a.id, a.title::text, a.summary::text, a.status::text, a.visibility::text, a.current_version,
           a.created_by_id, a.created_by_name::text, a.last_edited_by_name::text, a.updated_utc
    FROM knowledge.article a
    WHERE (p_search IS NULL OR a.title ILIKE '%' || p_search || '%' OR a.summary ILIKE '%' || p_search || '%')
      AND (p_status IS NULL OR a.status = p_status)
      AND (p_visibility IS NULL OR a.visibility = p_visibility)
      AND (p_mine IS NULL OR a.created_by_id = p_mine
           OR EXISTS (SELECT 1 FROM knowledge.article_editor e WHERE e.article_id = a.id AND e.user_id = p_mine))
    ORDER BY
        CASE WHEN p_sort = 'title' AND NOT p_desc THEN a.title END ASC,
        CASE WHEN p_sort = 'title' AND p_desc     THEN a.title END DESC,
        CASE WHEN p_sort = 'status' AND NOT p_desc THEN a.status END ASC,
        CASE WHEN p_sort = 'status' AND p_desc     THEN a.status END DESC,
        CASE WHEN p_sort = 'visibility' AND NOT p_desc THEN a.visibility END ASC,
        CASE WHEN p_sort = 'visibility' AND p_desc     THEN a.visibility END DESC,
        CASE WHEN p_sort = 'author' AND NOT p_desc THEN a.created_by_name END ASC,
        CASE WHEN p_sort = 'author' AND p_desc     THEN a.created_by_name END DESC,
        CASE WHEN p_sort = 'updated' AND NOT p_desc THEN a.updated_utc END ASC,
        CASE WHEN p_sort IS DISTINCT FROM 'updated' OR p_desc THEN a.updated_utc END DESC,
        a.id DESC
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;

CREATE OR REPLACE FUNCTION knowledge.count_articles(p_search text, p_status text, p_visibility text, p_mine bigint)
RETURNS bigint
LANGUAGE sql STABLE
AS $$
    SELECT COUNT(*) FROM knowledge.article a
    WHERE (p_search IS NULL OR a.title ILIKE '%' || p_search || '%' OR a.summary ILIKE '%' || p_search || '%')
      AND (p_status IS NULL OR a.status = p_status)
      AND (p_visibility IS NULL OR a.visibility = p_visibility)
      AND (p_mine IS NULL OR a.created_by_id = p_mine
           OR EXISTS (SELECT 1 FROM knowledge.article_editor e WHERE e.article_id = a.id AND e.user_id = p_mine));
$$;
