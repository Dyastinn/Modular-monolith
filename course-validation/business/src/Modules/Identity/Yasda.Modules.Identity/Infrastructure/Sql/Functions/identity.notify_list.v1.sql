-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- Who hears about work nobody has taken yet (decision D9). With a team: its leads (members whose role is TeamLead)
-- always, plus every member when the team's mode is Everyone, or the members flagged to be told when it is
-- LeadsAndSelected. With no team (unrouted work): every team lead. Only active accounts.
CREATE OR REPLACE FUNCTION identity.notify_list(p_team_id bigint)
RETURNS TABLE (user_id bigint)
LANGUAGE sql STABLE
AS $$
    SELECT u.id
    FROM identity.users u
    JOIN identity.user_roles ur ON ur.user_id = u.id
    JOIN identity.roles r ON r.id = ur.role_id
    WHERE p_team_id IS NULL AND r.name = 'TeamLead' AND NOT u.is_disabled
    UNION
    SELECT u.id
    FROM identity.team t
    JOIN identity.team_member m ON m.team_id = t.id
    JOIN identity.users u ON u.id = m.user_id
    JOIN identity.user_roles ur ON ur.user_id = u.id
    JOIN identity.roles r ON r.id = ur.role_id
    WHERE t.id = p_team_id AND NOT u.is_disabled
      AND (r.name = 'TeamLead' OR t.notify_mode = 'Everyone' OR (t.notify_mode = 'LeadsAndSelected' AND m.notify))
    ORDER BY 1;
$$;
