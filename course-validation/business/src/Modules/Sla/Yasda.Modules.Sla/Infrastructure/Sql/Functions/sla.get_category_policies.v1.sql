-- The category policies: for each category and priority that has its own numbers, in category order, most urgent first. Targets are whole minutes.
CREATE OR REPLACE FUNCTION sla.get_category_policies()
RETURNS TABLE (category_id bigint, priority text, respond_minutes integer, resolve_minutes integer, updated_utc timestamptz,
               updated_by_id bigint, updated_by_name text)
LANGUAGE sql STABLE
AS $$
    SELECT p.category_id, p.priority::text, p.respond_minutes, p.resolve_minutes, p.updated_utc, p.updated_by_id, p.updated_by_name::text
    FROM sla.category_policy p
    ORDER BY p.category_id,
             CASE p.priority WHEN 'Critical' THEN 0 WHEN 'High' THEN 1 WHEN 'Normal' THEN 2 ELSE 3 END;
$$;
