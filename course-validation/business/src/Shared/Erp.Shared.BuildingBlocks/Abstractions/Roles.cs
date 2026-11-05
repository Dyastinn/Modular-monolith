namespace Erp.Shared.BuildingBlocks;

// The role names, defined once. A typo in a role string silently breaks an auth check, so nobody types them.
public static class Roles
{
    public const string SalesRep = "SalesRep";
    public const string SalesManager = "SalesManager";
    public const string Admin = "Admin";
}
