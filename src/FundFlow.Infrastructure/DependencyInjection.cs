using FundFlow.Application.Common.Abstractions;
using FundFlow.Infrastructure.Auditing;
using FundFlow.Infrastructure.BackgroundJobs;
using FundFlow.Infrastructure.Caching;
using FundFlow.Infrastructure.Email;
using FundFlow.Infrastructure.Messaging;
using FundFlow.Infrastructure.Persistence;
using FundFlow.Infrastructure.Persistence.Interceptors;
using FundFlow.Infrastructure.Security;
using FundFlow.Infrastructure.Tenancy;
using Hangfire;
using Hangfire.SqlServer;
using MassTransit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace FundFlow.Infrastructure;

public static class DependencyInjection
{
    public const string SqlConnectionName = "Default";
    public const string RedisConnectionName = "Redis";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddOptions<SecurityOptions>().Bind(configuration.GetSection(SecurityOptions.SectionName));
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.SectionName));
        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.SectionName));
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => o.SigningKey.Length >= 32, "Jwt:SigningKey must be at least 32 characters. Provide it via an environment variable or secret store.")
            .ValidateOnStart();

        services.AddPersistence(configuration);
        services.AddCaching(configuration);
        services.AddSecurityServices();
        services.AddMessaging(configuration);
        services.AddBackgroundJobs(configuration);

        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();

        return services;
    }

    private static void AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(SqlConnectionName)
                               ?? throw new InvalidOperationException($"Connection string '{SqlConnectionName}' is not configured.");

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantScope>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantDirectory, TenantDirectory>();

        services.AddScoped<EntitySaveInterceptor>();
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsHistoryTable("__EFMigrationsHistory", "dbo");
                sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
                sql.CommandTimeout(30);
            });
            options.AddInterceptors(sp.GetRequiredService<EntitySaveInterceptor>());
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddSingleton<DatabaseInitializer>();
    }

    private static void AddCaching(this IServiceCollection services, IConfiguration configuration)
    {
        var redis = configuration.GetConnectionString(RedisConnectionName);
        if (string.IsNullOrWhiteSpace(redis))
        {
            // No Redis configured (unit/integration tests, minimal local runs): a per-process cache is fine.
            services.AddDistributedMemoryCache();
        }
        else
        {
            services.AddStackExchangeRedisCache(options =>
            {
                var parsed = ConfigurationOptions.Parse(redis);
                parsed.AbortOnConnectFail = false; // the API must start even if Redis is briefly unavailable
                options.ConfigurationOptions = parsed;
                options.InstanceName = "fundflow:";
            });
        }

        services.AddSingleton<ICacheService, CacheService>();
    }

    private static void AddSecurityServices(this IServiceCollection services)
    {
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<ISecretTokens, SecretTokens>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<IUserAccessProvider, UserAccessProvider>();

        services.AddDataProtection()
            .SetApplicationName("FundFlow")
            .PersistKeysToDbContext<AppDbContext>();
        services.AddSingleton<IEmailPayloadProtector, EmailPayloadProtector>();
    }

    private static void AddMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var transport = configuration["Messaging:Transport"] ?? "RabbitMq";

        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();
            x.AddConsumer<SendTransactionalEmailConsumer>();

            // Transactional outbox: messages are written in the same transaction as the business change and
            // delivered to the broker afterwards, so a crash can neither lose nor phantom-send them.
            x.AddEntityFrameworkOutbox<AppDbContext>(o =>
            {
                o.UseSqlServer();
                o.UseBusOutbox();
                o.QueryDelay = TimeSpan.FromSeconds(1);
                o.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);
            });

            x.AddConfigureEndpointsCallback((context, _, endpoint) =>
            {
                endpoint.UseMessageRetry(r => r.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(5)));
                endpoint.UseEntityFrameworkOutbox<AppDbContext>(context); // inbox: consumers are idempotent per MessageId
            });

            if (string.Equals(transport, "InMemory", StringComparison.OrdinalIgnoreCase))
            {
                x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
            }
            else
            {
                x.UsingRabbitMq((context, cfg) =>
                {
                    var section = configuration.GetSection("RabbitMq");
                    cfg.Host(
                        section["Host"] ?? "localhost",
                        ushort.TryParse(section["Port"], out var port) ? port : (ushort)5672,
                        section["VirtualHost"] ?? "/",
                        h =>
                        {
                            h.Username(section["Username"] ?? "guest");
                            h.Password(section["Password"] ?? "guest");
                        });
                    cfg.ConfigureEndpoints(context);
                });
            }
        });

        // The API must come up even if the broker is briefly unavailable; the outbox retries delivery.
        services.Configure<MassTransitHostOptions>(o =>
        {
            o.WaitUntilStarted = false;
            o.StartTimeout = TimeSpan.FromSeconds(30);
        });

        services.AddScoped<IEmailQueue, OutboxEmailQueue>();
        services.AddScoped<IIntegrationEventPublisher, OutboxIntegrationEventPublisher>();
    }

    private static void AddBackgroundJobs(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<CleanupExpiredSessionsJob>();

        if (!configuration.GetValue("Hangfire:Enabled", true))
        {
            return;
        }

        var connectionString = configuration.GetConnectionString(SqlConnectionName)!;
        services.AddHangfire(hangfire => hangfire
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
            {
                SchemaName = Schemas.Hangfire,
                PrepareSchemaIfNecessary = configuration.GetValue("Hangfire:PrepareSchema", true),
                DisableGlobalLocks = true,
                UseRecommendedIsolationLevel = true,
            }));

        services.AddHangfireServer(options =>
        {
            options.ServerName = $"fundflow-{Environment.MachineName}";
            options.WorkerCount = configuration.GetValue("Hangfire:WorkerCount", Math.Max(2, Environment.ProcessorCount));
            options.Queues = ["default"];
        });
    }

    /// <summary>Registers the recurring jobs. Called once at startup; re-registering just updates the schedule.</summary>
    public static void RegisterRecurringJobs(IRecurringJobManager recurringJobs)
    {
        recurringJobs.AddOrUpdate<CleanupExpiredSessionsJob>(
            CleanupExpiredSessionsJob.JobId,
            job => job.RunAsync(CancellationToken.None),
            Cron.Daily(3));
    }
}
