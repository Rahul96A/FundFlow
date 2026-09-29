using FluentValidation;
using FundFlow.Application.Common.Abstractions;
using FundFlow.Application.Common.Exceptions;
using FundFlow.Application.Common.Paging;
using FundFlow.Contracts.Common;
using FundFlow.Contracts.Identity;
using FundFlow.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FundFlow.Application.Identity.Users;

public sealed record ListUsersQuery(UserStatus? Status, Guid? RoleId) : PagedQuery, IRequest<PagedResponse<UserSummaryResponse>>;

public sealed class ListUsersHandler(IAppDbContext db, TimeProvider clock)
    : IRequestHandler<ListUsersQuery, PagedResponse<UserSummaryResponse>>
{
    private static readonly SortMap<User> Sorts = new SortMap<User>()
        .Add("name", u => u.LastName)
        .Add("email", u => u.Email)
        .Add("lastLoginAt", u => u.LastLoginAt)
        .Add("createdAt", u => u.CreatedAt)
        .Default("createdAt", descending: true);

    public async Task<PagedResponse<UserSummaryResponse>> Handle(ListUsersQuery request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var query = db.Users.AsNoTracking();

        if (request.SearchTerm is { } term)
        {
            var pattern = $"%{PagingExtensions.EscapeLike(term)}%";
            query = query.Where(u =>
                EF.Functions.Like(u.Email, pattern, "\\")
                || EF.Functions.Like(u.FirstName, pattern, "\\")
                || EF.Functions.Like(u.LastName, pattern, "\\")
                || EF.Functions.Like(u.FirstName + " " + u.LastName, pattern, "\\"));
        }

        if (request.Status is { } status)
        {
            query = query.Where(IdentityQueries.HasStatus(status, now));
        }

        if (request.RoleId is { } roleId)
        {
            query = query.Where(u => u.UserRoles.Any(ur => ur.RoleId == roleId));
        }

        var page = await Sorts.Apply(query, request.SortBy, request.SortDirection)
            .ToPagedResponseAsync(IdentityQueries.ToRow, request, cancellationToken);

        return PagedResponse<UserSummaryResponse>.Create(
            page.Items.Select(r => r.ToSummary(now)).ToList(),
            page.Page,
            page.PageSize,
            page.TotalCount);
    }
}

public sealed record GetUserQuery(Guid UserId) : IRequest<UserDetailResponse>;

public sealed class GetUserHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<GetUserQuery, UserDetailResponse>
{
    public async Task<UserDetailResponse> Handle(GetUserQuery request, CancellationToken cancellationToken) =>
        await db.GetUserDetailAsync(request.UserId, clock.GetUtcNow(), cancellationToken)
        ?? throw new NotFoundException(nameof(User), request.UserId);
}

public sealed class ListUsersValidator : AbstractValidator<ListUsersQuery>
{
    public ListUsersValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
    }
}
