using FundFlow.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FundFlow.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> create migrations without booting the whole API host. Migration generation never opens a
/// connection, so the default connection string is only a placeholder; set FUNDFLOW_CONNECTION_STRING to run
/// <c>dotnet ef database update</c> or to script migrations against a real server.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("FUNDFLOW_CONNECTION_STRING")
                               ?? "Server=localhost,1433;Database=FundFlow;User Id=sa;Password=design-time-placeholder;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "dbo"))
            .Options;

        return new AppDbContext(options, new NoTenantContext());
    }
}
