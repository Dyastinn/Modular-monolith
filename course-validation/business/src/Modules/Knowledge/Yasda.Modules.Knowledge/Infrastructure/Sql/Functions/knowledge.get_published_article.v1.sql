-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- One article as readers see it: the newest published version. No row when the article doesn't exist, was never published, is
-- archived, or is Internal and p_public_only is set: a reader can't tell those apart.
CREATE OR REPLACE FUNCTION knowledge.get_published_article(p_id bigint, p_public_only boolean)
RETURNS TABLE (id bigint, title text, summary text, body text, visibility text, version_number integer, published_utc timestamptz)
LANGUAGE sql STABLE
AS $$
    SELECT a.id, v.title::text, v.summary::text, v.body::text, v.visibility::text, v.number, v.published_utc
    FROM knowledge.article a
    JOIN knowledge.article_version v ON v.article_id = a.id AND v.number = a.current_version
    WHERE a.id = p_id
      AND a.status <> 'Archived' AND a.current_version > 0
      AND (NOT p_public_only OR v.visibility = 'Public');
$$;
