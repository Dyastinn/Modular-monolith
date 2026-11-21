-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- The most read articles since a date, among the ones a reader may see (the same rule as the reader list). Ties go to the
-- more recently published.
CREATE OR REPLACE FUNCTION knowledge.popular_articles(p_since date, p_public_only boolean, p_limit integer)
RETURNS TABLE (id bigint, title text, summary text, views bigint)
LANGUAGE sql STABLE
AS $$
    SELECT a.id, v.title::text, v.summary::text, SUM(d.views)::bigint AS views
    FROM knowledge.article_view_day d
    JOIN knowledge.article a ON a.id = d.article_id
    JOIN knowledge.article_version v ON v.article_id = a.id AND v.number = a.current_version
    WHERE d.day >= p_since
      AND a.status <> 'Archived' AND a.current_version > 0
      AND (NOT p_public_only OR v.visibility = 'Public')
    GROUP BY a.id, v.title, v.summary, v.published_utc
    ORDER BY SUM(d.views) DESC, v.published_utc DESC, a.id
    LIMIT p_limit;
$$;
