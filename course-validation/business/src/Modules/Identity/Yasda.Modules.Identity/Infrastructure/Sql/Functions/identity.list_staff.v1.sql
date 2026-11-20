-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- Active accounts that may handle tickets, by name: the "Assign to" list and IUserDirectory.FindStaffAsync. With a team
-- number, only that team's members.
CREATE OR REPLACE FUNCTION identity.list_staff(p_id bigint, p_team_id bigint)
RETURNS TABLE (id bigint, full_name text, role text)
LANGUAGE sql STABLE
AS $$
    SELECT u.id, u.full_name::text, r.name::text
    FROM identity.users u
    JOIN identity.user_roles ur ON ur.user_id = u.id
    JOIN identity.roles r ON r.id = ur.role_id
    WHERE r.name IN ('Agent', 'TeamLead', 'Admin') AND NOT u.is_disabled
      AND (p_id IS NULL OR u.id = p_id)
      AND (p_team_id IS NULL OR EXISTS (SELECT 1 FROM identity.team_member m WHERE m.team_id = p_team_id AND m.user_id = u.id))
    ORDER BY u.full_name, u.id;
$$;
