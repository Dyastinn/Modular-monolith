-- The seven weekdays, Monday first. A closed day has no times.
CREATE OR REPLACE FUNCTION sla.get_calendar_days()
RETURNS TABLE (day_of_week text, opens time, closes time)
LANGUAGE sql STABLE
AS $$
    SELECT d.day::text, d.opens, d.closes
    FROM sla.work_day d
    ORDER BY CASE d.day WHEN 'Monday' THEN 1 WHEN 'Tuesday' THEN 2 WHEN 'Wednesday' THEN 3 WHEN 'Thursday' THEN 4
                        WHEN 'Friday' THEN 5 WHEN 'Saturday' THEN 6 ELSE 7 END;
$$;
