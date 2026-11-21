-- The automatic rules and who changed them last. A rule with no number is off. There is one row.
CREATE OR REPLACE FUNCTION tickets.get_automation_settings()
RETURNS TABLE (close_resolved_after_days integer, remind_pending_after_days integer, close_pending_after_days integer,
               updated_utc timestamptz, updated_by_id bigint, updated_by_name text)
LANGUAGE sql STABLE
AS $$
    SELECT s.close_resolved_after_days, s.remind_pending_after_days, s.close_pending_after_days,
           s.updated_utc, s.updated_by_id, s.updated_by_name::text
    FROM tickets.automation_settings s
    ORDER BY s.id
    LIMIT 1;
$$;
