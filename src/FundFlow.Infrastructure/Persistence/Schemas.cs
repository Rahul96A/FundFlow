namespace FundFlow.Infrastructure.Persistence;

/// <summary>
/// One SQL Server schema per module. Modules never join across each other's tables in application code, so a module
/// can later be extracted into its own database without a rewrite.
/// </summary>
public static class Schemas
{
    public const string Identity = "identity";
    public const string Organizations = "organizations";
    public const string Audit = "audit";
    public const string Messaging = "messaging";
    public const string Security = "security";
    public const string Hangfire = "hangfire";
}
