-- Released scripts are never edited: change a function with a .v2 file and a new migration.
CREATE OR REPLACE FUNCTION purchasing.get_purchasing_settings()
RETURNS TABLE (approval_threshold numeric, updated_by bigint, updated_utc timestamptz)
LANGUAGE sql STABLE
AS $$
    SELECT s.approval_threshold, s.updated_by, s.updated_utc FROM purchasing.purchasing_settings s WHERE s.id = 1;
$$;
