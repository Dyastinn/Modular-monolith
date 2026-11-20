-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- One team, and its members with their roles. A disabled account stays listed (so an admin can remove it) and says so.
CREATE OR REPLACE FUNCTION identity.get_team(p_id bigint)
RETURNS TABLE (id bigint, name text, is_active boolean, notify_mode text)
LANGUAGE sql STABLE
AS $$
    SELECT t.id, t.name::text, t.is_active, t.notify_mode::text
    FROM identity.team t
    WHERE t.id = p_id;
$$;

CREATE OR REPLACE FUNCTION identity.get_team_members(p_team_id bigint)
RETURNS TABLE (user_id bigint, full_name text, role text, notify boolean, is_disabled boolean)
LANGUAGE sql STABLE
AS $$
    SELECT m.user_id, u.full_name::text, COALESCE(r.name, '')::text, m.notify, u.is_disabled
    FROM identity.team_member m
    JOIN identity.users u ON u.id = m.user_id
    LEFT JOIN identity.user_roles ur ON ur.user_id = u.id
    LEFT JOIN identity.roles r ON r.id = ur.role_id
    WHERE m.team_id = p_team_id
    ORDER BY u.full_name, u.id;
$$;
