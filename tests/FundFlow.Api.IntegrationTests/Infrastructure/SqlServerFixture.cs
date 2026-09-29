using Testcontainers.MsSql;

namespace FundFlow.Api.IntegrationTests.Infrastructure;

/// <summary>
/// One SQL Server container per test run, one API host on top of it. Tests isolate themselves by creating their own
/// organizations (unique names, slugs and emails) rather than by resetting the database, which keeps the suite fast
/// and, usefully, means every test runs alongside other tenants' data.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("Integration_Tests#2026")
        .Build();

    public ApiFactory Factory { get; private set; } = default!;

    public string ServerConnectionString => _sql.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();
        Factory = new ApiFactory(_sql.GetConnectionString());
        await Factory.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        await _sql.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
