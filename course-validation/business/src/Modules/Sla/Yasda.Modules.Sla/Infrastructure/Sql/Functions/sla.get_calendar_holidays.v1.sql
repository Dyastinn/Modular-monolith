-- The holidays, earliest first.
CREATE OR REPLACE FUNCTION sla.get_calendar_holidays()
RETURNS TABLE (holiday_date date, name text)
LANGUAGE sql STABLE
AS $$
    SELECT h.date, h.name::text
    FROM sla.holiday h
    ORDER BY h.date;
$$;
