using System.Collections.Concurrent;
using System.Net;
using FundFlow.Api.IntegrationTests.Infrastructure;
using FundFlow.Contracts.IntegrationEvents;
using FundFlow.Domain.Identity;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FundFlow.Api.IntegrationTests;

/// <summary>
/// The transactional outbox is the backbone of reliable side effects: a message exists if and only if the business
/// change that caused it was committed, and it is delivered afterwards.
/// </summary>
[Collection(ApiCollection.Name)]
public class MessagingTests(ApiFixture fixture)
{
    private readonly TestKit _kit = new(fixture.Factory);

    private sealed class Probe
    {
        public ConcurrentBag<OrganizationRegisteredIntegrationEvent> Organizations { get; } = [];

        public ConcurrentBag<UserCreatedIntegrationEvent> Users { get; } = [];
    }

    private async Task<(Probe Probe, HostReceiveEndpointHandle Handle)> ListenAsync()
    {
        var probe = new Probe();
        var bus = fixture.Factory.Services.GetRequiredService<IBusControl>();
        var handle = bus.ConnectReceiveEndpoint($"probe-{Guid.NewGuid():N}", endpoint =>
        {
            endpoint.Handler<OrganizationRegisteredIntegrationEvent>(context =>
            {
                probe.Organizations.Add(context.Message);
                return Task.CompletedTask;
            });
            endpoint.Handler<UserCreatedIntegrationEvent>(context =>
            {
                probe.Users.Add(context.Message);
                return Task.CompletedTask;
            });
        });
        await handle.Ready;
        return (probe, handle);
    }

    private static async Task<T> EventuallyAsync<T>(Func<T?> read)
        where T : class
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (read() is { } value)
            {
                return value;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Expected {typeof(T).Name} never arrived.");
    }

    [Fact]
    public async Task Registering_an_organization_publishes_integration_events_through_the_outbox()
    {
        var (probe, handle) = await ListenAsync();
        try
        {
            var org = await _kit.CreateOrganizationAsync();

            var registered = await EventuallyAsync(() => probe.Organizations.FirstOrDefault(e => e.OrganizationId == org.Id));
            registered.Slug.Should().Be(org.Slug);
            registered.Name.Should().Be(org.Name);

            var created = await EventuallyAsync(() => probe.Users.FirstOrDefault(e => e.Email == org.OwnerEmail));
            created.TenantId.Should().Be(org.Id);
            created.OccurredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        }
        finally
        {
            await handle.StopAsync();
        }
    }

    [Fact]
    public async Task Inviting_a_user_publishes_UserCreated_for_the_new_user_in_their_tenant()
    {
        var (probe, handle) = await ListenAsync();
        try
        {
            var org = await _kit.CreateOrganizationAsync();
            var user = await _kit.CreateUserAsync(org, SystemRoles.Staff);

            var created = await EventuallyAsync(() => probe.Users.FirstOrDefault(e => e.UserId == user.Id));

            created.TenantId.Should().Be(org.Id);
            created.Email.Should().Be(user.Email);
        }
        finally
        {
            await handle.StopAsync();
        }
    }

    [Fact]
    public async Task Nothing_is_published_or_emailed_when_the_business_operation_fails()
    {
        var org = await _kit.CreateOrganizationAsync();
        var existing = await _kit.CreateUserAsync(org, SystemRoles.Staff);
        var emailsBefore = fixture.Factory.Emails.Sent.Count;
        var staff = await _kit.RoleIdAsync(org.Owner, SystemRoles.Staff);
        var (probe, handle) = await ListenAsync();
        try
        {
            // A duplicate invitation fails validation before anything is staged...
            using var duplicate = await org.Owner.PostAsync("/api/v1/users", new FundFlow.Contracts.Identity.InviteUserRequest(existing.Email, "Dup", "Licate", null, [staff]));
            await duplicate.ShouldBeAsync(HttpStatusCode.Conflict);

            // ...and one that names a role that does not exist fails after the user is built but before commit.
            var email = $"ghost-{TestKit.Unique("")}@example.test";
            using var badRole = await org.Owner.PostAsync("/api/v1/users", new FundFlow.Contracts.Identity.InviteUserRequest(email, "Gho", "St", null, [Guid.NewGuid()]));
            await badRole.ShouldBeAsync(HttpStatusCode.NotFound);

            await Task.Delay(TimeSpan.FromSeconds(3)); // longer than the outbox delivery interval
            fixture.Factory.Emails.Sent.Count.Should().Be(emailsBefore, "a rolled-back operation must not send email");
            fixture.Factory.Emails.CountFor(email).Should().Be(0);
            probe.Users.Should().NotContain(u => u.Email == email);
        }
        finally
        {
            await handle.StopAsync();
        }
    }

    [Fact]
    public async Task The_outbox_drains_completely_once_messages_are_delivered()
    {
        var org = await _kit.CreateOrganizationAsync();
        await _kit.CreateUserAsync(org, SystemRoles.Staff);

        // Delivered messages are removed from the outbox, so an empty table means nothing is stuck.
        var pending = int.MaxValue;
        for (var i = 0; i < 50 && pending > 0; i++)
        {
            pending = await _kit.WithDbAsync(null, async (db, _) =>
                await db.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>().CountAsync());
            if (pending > 0)
            {
                await Task.Delay(200);
            }
        }

        pending.Should().Be(0, "every message written to the outbox must eventually be delivered and removed");
    }
}
