-- The active accounts that have a role, by name. Other modules ask for it through IUserDirectory.UsersInRoleAsync.
CREATE OR REPLACE FUNCTION identity.users_in_role(p_role text)
RETURNS TABLE (user_id bigint)
LANGUAGE sql STABLE
AS $$
    SELECT u.id
    FROM identity.users u
    JOIN identity.user_roles ur ON ur.user_id = u.id
    JOIN identity.roles r ON r.id = ur.role_id
    WHERE r.name = p_role AND NOT u.is_disabled
    ORDER BY u.id;
$$;
