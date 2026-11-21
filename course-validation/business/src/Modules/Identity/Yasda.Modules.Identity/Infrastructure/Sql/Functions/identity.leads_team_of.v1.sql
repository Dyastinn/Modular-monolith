-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- Is the first person a team lead in a team the second person belongs to? ITeamDirectory.LeadsTeamOfAsync, for who may edit
-- someone's knowledge-base draft. Only an active account counts as a lead.
CREATE OR REPLACE FUNCTION identity.leads_team_of(p_lead_id bigint, p_member_id bigint)
RETURNS boolean
LANGUAGE sql STABLE
AS $$
    SELECT EXISTS (
        SELECT 1
        FROM identity.team_member lead_row
        JOIN identity.team_member member_row ON member_row.team_id = lead_row.team_id
        JOIN identity.users u ON u.id = lead_row.user_id
        JOIN identity.user_roles ur ON ur.user_id = u.id
        JOIN identity.roles r ON r.id = ur.role_id
        WHERE lead_row.user_id = p_lead_id
          AND member_row.user_id = p_member_id
          AND r.name = 'TeamLead'
          AND NOT u.is_disabled
    );
$$;
