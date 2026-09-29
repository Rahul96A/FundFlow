using System.Text.Json;
using FluentValidation;
using FundFlow.Application.Common.Abstractions;
using FundFlow.Application.Common.Paging;
using FundFlow.Contracts.Audit;
using FundFlow.Contracts.Common;
using FundFlow.Domain.Audit;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FundFlow.Application.Audit;

public sealed record ListAuditLogsQuery(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Action,
    string? EntityType,
    Guid? UserId) : PagedQuery, IRequest<PagedResponse<AuditLogResponse>>;

public sealed class ListAuditLogsValidator : AbstractValidator<ListAuditLogsQuery>
{
    public ListAuditLogsValidator()
    {
        RuleFor(x => x.Action).MaximumLength(100);
        RuleFor(x => x.EntityType).MaximumLength(100);
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From!.Value).When(x => x.From.HasValue && x.To.HasValue)
            .WithMessage("The end date must not be before the start date.");
    }
}

public sealed class ListAuditLogsHandler(IAppDbContext db)
    : IRequestHandler<ListAuditLogsQuery, PagedResponse<AuditLogResponse>>
{
    private static readonly SortMap<AuditLog> Sorts = new SortMap<AuditLog>()
        .Add("timestamp", a => a.Timestamp)
        .Add("action", a => a.Action)
        .Add("entityType", a => a.EntityType)
        .Add("userEmail", a => a.UserEmail)
        .Default("timestamp", descending: true);

    public async Task<PagedResponse<AuditLogResponse>> Handle(ListAuditLogsQuery request, CancellationToken cancellationToken)
    {
        var query = db.AuditLogs.AsNoTracking();

        if (request.From is { } from)
        {
            query = query.Where(a => a.Timestamp >= from);
        }

        if (request.To is { } to)
        {
            query = query.Where(a => a.Timestamp <= to);
        }

        if (!string.IsNullOrWhiteSpace(request.Action))
        {
            var action = request.Action.Trim();
            // "Auth." selects the whole module; "Auth.Login" is an exact match.
            query = action.EndsWith('.')
                ? query.Where(a => a.Action.StartsWith(action))
                : query.Where(a => a.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(request.EntityType))
        {
            var entityType = request.EntityType.Trim();
            query = query.Where(a => a.EntityType == entityType);
        }

        if (request.UserId is { } userId)
        {
            query = query.Where(a => a.UserId == userId);
        }

        if (request.SearchTerm is { } term)
        {
            var pattern = $"%{PagingExtensions.EscapeLike(term)}%";
            query = query.Where(a =>
                (a.UserEmail != null && EF.Functions.Like(a.UserEmail, pattern, "\\"))
                || EF.Functions.Like(a.Action, pattern, "\\")
                || EF.Functions.Like(a.EntityType, pattern, "\\")
                || (a.EntityId != null && EF.Functions.Like(a.EntityId, pattern, "\\")));
        }

        var page = await Sorts.Apply(query, request.SortBy, request.SortDirection)
            .ToPagedResponseAsync(
                a => new Row(a.Id, a.Timestamp, a.UserId, a.UserEmail, a.Action, a.EntityType, a.EntityId, a.IpAddress, a.UserAgent, a.OldValues, a.NewValues, a.CorrelationId),
                request,
                cancellationToken);

        return PagedResponse<AuditLogResponse>.Create(
            page.Items.Select(ToResponse).ToList(),
            page.Page,
            page.PageSize,
            page.TotalCount);
    }

    private static AuditLogResponse ToResponse(Row r) => new(
        r.Id, r.Timestamp, r.UserId, r.UserEmail, r.Action, r.EntityType, r.EntityId, r.IpAddress, r.UserAgent,
        ParseJson(r.OldValues), ParseJson(r.NewValues), r.CorrelationId);

    private static JsonElement? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Row(
        Guid Id,
        DateTimeOffset Timestamp,
        Guid? UserId,
        string? UserEmail,
        string Action,
        string EntityType,
        string? EntityId,
        string? IpAddress,
        string? UserAgent,
        string? OldValues,
        string? NewValues,
        string? CorrelationId);
}
