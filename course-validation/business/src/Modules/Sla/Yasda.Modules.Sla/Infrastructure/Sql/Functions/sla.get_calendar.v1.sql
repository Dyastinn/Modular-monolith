-- The organization's calendar: its time zone and who changed it last. There is one row.
CREATE OR REPLACE FUNCTION sla.get_calendar()
RETURNS TABLE (time_zone_id text, updated_utc timestamptz, updated_by_id bigint, updated_by_name text)
LANGUAGE sql STABLE
AS $$
    SELECT c.time_zone_id::text, c.updated_utc, c.updated_by_id, c.updated_by_name::text
    FROM sla.work_calendar c
    ORDER BY c.id
    LIMIT 1;
$$;
