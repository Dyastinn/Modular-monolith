-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- The teams a person is a member of: ITeamDirectory.TeamIdsOfAsync, and the queue's "My teams" filter.
CREATE OR REPLACE FUNCTION identity.teams_of_user(p_user_id bigint)
RETURNS TABLE (team_id bigint)
LANGUAGE sql STABLE
AS $$
    SELECT m.team_id FROM identity.team_member m WHERE m.user_id = p_user_id ORDER BY m.team_id;
$$;
