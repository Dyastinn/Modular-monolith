-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- What readers can look up: each article's newest published version, never the working copy being revised. An article is
-- listed when it has been published and is not archived. p_public_only hides the Internal ones (the handler passes it for
-- anyone who is not staff). Both the filter and the words come from the version, so a revision in progress changes nothing here.
CREATE OR REPLACE FUNCTION knowledge.search_published_articles(
    p_search text, p_public_only boolean, p_sort text, p_desc boolean, p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, title text, summary text, visibility text, published_utc timestamptz)
LANGUAGE sql STABLE
AS $$
    SELECT a.id, v.title::text, v.summary::text, v.visibility::text, v.published_utc
    FROM knowledge.article a
    JOIN knowledge.article_version v ON v.article_id = a.id AND v.number = a.current_version
    WHERE a.status <> 'Archived' AND a.current_version > 0
      AND (NOT p_public_only OR v.visibility = 'Public')
      AND (p_search IS NULL OR v.title ILIKE '%' || p_search || '%' OR v.summary ILIKE '%' || p_search || '%')
    ORDER BY
        CASE WHEN p_sort = 'title' AND NOT p_desc THEN v.title END ASC,
        CASE WHEN p_sort = 'title' AND p_desc     THEN v.title END DESC,
        CASE WHEN p_sort = 'updated' AND NOT p_desc THEN v.published_utc END ASC,
        CASE WHEN p_sort IS DISTINCT FROM 'updated' OR p_desc THEN v.published_utc END DESC,
        a.id DESC
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;

CREATE OR REPLACE FUNCTION knowledge.count_published_articles(p_search text, p_public_only boolean)
RETURNS bigint
LANGUAGE sql STABLE
AS $$
    SELECT COUNT(*)
    FROM knowledge.article a
    JOIN knowledge.article_version v ON v.article_id = a.id AND v.number = a.current_version
    WHERE a.status <> 'Archived' AND a.current_version > 0
      AND (NOT p_public_only OR v.visibility = 'Public')
      AND (p_search IS NULL OR v.title ILIKE '%' || p_search || '%' OR v.summary ILIKE '%' || p_search || '%');
$$;
