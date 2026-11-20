-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- Priorities sort by urgency, not alphabetically ("High" < "Low" < "Normal" < "Critical" would be nonsense).
CREATE OR REPLACE FUNCTION tickets.priority_rank(p_priority text)
RETURNS integer
LANGUAGE sql IMMUTABLE
AS $$
    SELECT CASE p_priority WHEN 'Low' THEN 0 WHEN 'Normal' THEN 1 WHEN 'High' THEN 2 WHEN 'Critical' THEN 3 END;
$$;
