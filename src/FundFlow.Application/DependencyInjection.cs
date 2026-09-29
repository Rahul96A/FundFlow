using FluentValidation;
using FundFlow.Application.Common.Abstractions;
using FundFlow.Application.Common.Behaviors;
using FundFlow.Application.Common.DomainEvents;
using FundFlow.Application.Common.Options;
using FundFlow.Application.Common.Tenancy;
using FundFlow.Application.Identity.Services;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FundFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddOptions<AppOptions>().Bind(configuration.GetSection(AppOptions.SectionName));
        services.AddOptions<AuthOptions>().Bind(configuration.GetSection(AuthOptions.SectionName));

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            // Order: logging wraps validation so that rejected requests are still timed.
            cfg.AddOpenBehavior(typeof(RequestLoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        services.AddScoped<IDomainEventDispatcher, MediatRDomainEventDispatcher>();
        services.AddScoped<ITenantProvider, TenantProvider>();
        services.AddScoped<IUserTokenService, UserTokenService>();
        services.AddScoped<IRoleAssignmentGuard, RoleAssignmentGuard>();
        services.AddScoped<ISessionRevocations, SessionRevocations>();

        return services;
    }
}
