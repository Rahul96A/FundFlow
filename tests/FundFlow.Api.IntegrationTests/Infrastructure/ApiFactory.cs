using FundFlow.Application.Common.Abstractions;
using FundFlow.Infrastructure.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FundFlow.Api.IntegrationTests.Infrastructure;

/// <summary>
/// The real API host (real EF Core migrations, real MassTransit outbox and consumers, real authentication) against a
/// real SQL Server, with exactly two seams replaced: the SMTP sender (so tests can read emails) and the transport
/// (in-memory instead of RabbitMQ; the outbox in front of it is the production one).
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string SuperAdminEmail = "superadmin@fundflow.test";
    public const string SuperAdminPassword = "Platform-Operator-Passw0rd!";

    private readonly Dictionary<string, string?> _settings;

    public ApiFactory(string serverConnectionString, IDictionary<string, string?>? overrides = null)
    {
        var builder = new SqlConnectionStringBuilder(serverConnectionString)
        {
            InitialCatalog = $"fundflow_test_{Guid.NewGuid():N}",
        };

        _settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = builder.ConnectionString,
            ["ConnectionStrings:Redis"] = string.Empty, // in-memory distributed cache
            ["Messaging:Transport"] = "InMemory",
            ["Hangfire:Enabled"] = "false",
            ["RateLimiting:Enabled"] = "false",
            ["Database:AutoMigrate"] = "true",
            ["Database:SeedDemoData"] = "false",
            ["Security:PasswordHashIterations"] = "1000",
            ["Jwt:SigningKey"] = "integration-tests-only-signing-key-0123456789abcdef",
            ["Jwt:Issuer"] = "https://api.fundflow.test",
            ["Auth:RefreshReuseGraceSeconds"] = "2",
            ["Auth:MaxFailedAccessAttempts"] = "3",
            ["Auth:LockoutMinutes"] = "15",
            ["App:PublicBaseUrl"] = "https://app.fundflow.test",
            ["Swagger:Enabled"] = "true",
            ["Serilog:MinimumLevel:Default"] = "Warning",
            ["Serilog:MinimumLevel:Override:Microsoft"] = "Error",
            ["Serilog:MinimumLevel:Override:FundFlow.Api.GlobalExceptionHandler"] = "Fatal",
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                _settings[key] = value;
            }
        }
    }

    public CapturingEmailSender Emails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting feeds host configuration, which minimal-hosting Program.cs can read while registering services.
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);

            // Test-only fault-injection endpoints (TestFaultsController) live in this assembly.
            services.AddControllers().AddApplicationPart(typeof(ApiFactory).Assembly);
        });
    }

    /// <summary>Starts the host (running migrations) and provisions the platform operator.</summary>
    public async Task InitializeAsync()
    {
        _ = Server; // forces host creation: migrations and reference data run during startup

        var initializer = Services.GetRequiredService<FundFlow.Infrastructure.Persistence.DatabaseInitializer>();
        await initializer.EnsureSuperAdminAsync(SuperAdminEmail, SuperAdminPassword, CancellationToken.None);
    }
}
