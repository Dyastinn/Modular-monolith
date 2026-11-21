-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- One article for the staff page: the working copy. No row when there is no such article.
CREATE OR REPLACE FUNCTION knowledge.get_article(p_id bigint)
RETURNS TABLE (id bigint, title text, summary text, body text, status text, visibility text, current_version integer,
               created_by_id bigint, created_by_name text, last_edited_by_id bigint, last_edited_by_name text,
               created_utc timestamptz, updated_utc timestamptz, rejection_reason text)
LANGUAGE sql STABLE
AS $$
    SELECT a.id, a.title::text, a.summary::text, a.body::text, a.status::text, a.visibility::text, a.current_version,
           a.created_by_id, a.created_by_name::text, a.last_edited_by_id, a.last_edited_by_name::text,
           a.created_utc, a.updated_utc, a.rejection_reason::text
    FROM knowledge.article a
    WHERE a.id = p_id;
$$;

-- Every published version, newest first. The words of a version are not listed here: this is the table of who published what, when.
CREATE OR REPLACE FUNCTION knowledge.get_article_versions(p_id bigint)
RETURNS TABLE (number integer, title text, visibility text, published_by_id bigint, published_by_name text, published_utc timestamptz)
LANGUAGE sql STABLE
AS $$
    SELECT v.number, v.title::text, v.visibility::text, v.published_by_id, v.published_by_name::text, v.published_utc
    FROM knowledge.article_version v
    WHERE v.article_id = p_id
    ORDER BY v.number DESC;
$$;

-- The people on the article's own edit list, in the order they were added.
CREATE OR REPLACE FUNCTION knowledge.get_article_editors(p_id bigint)
RETURNS TABLE (user_id bigint, user_name text, added_by_name text, added_utc timestamptz)
LANGUAGE sql STABLE
AS $$
    SELECT e.user_id, e.user_name::text, e.added_by_name::text, e.added_utc
    FROM knowledge.article_editor e
    WHERE e.article_id = p_id
    ORDER BY e.added_utc, e.id;
$$;

-- The audit trail, oldest first.
CREATE OR REPLACE FUNCTION knowledge.get_article_history(p_id bigint)
RETURNS TABLE (action text, actor_id bigint, actor_name text, at_utc timestamptz, note text, version_number integer)
LANGUAGE sql STABLE
AS $$
    SELECT h.action::text, h.actor_id, h.actor_name::text, h.at_utc, h.note::text, h.version_number
    FROM knowledge.article_history h
    WHERE h.article_id = p_id
    ORDER BY h.at_utc, h.id;
$$;
