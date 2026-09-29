using System.Linq.Expressions;
using FundFlow.Contracts.Common;
using FundFlow.Domain.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace FundFlow.Application.Common.Paging;

/// <summary>Common paging, sorting and free-text search inputs shared by every list query.</summary>
public abstract record PagedQuery
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 25;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    public string? SortBy { get; init; }

    public SortDirection? SortDirection { get; init; }

    public string? Search { get; init; }

    public int SafePage => Math.Max(1, Page);

    public int SafePageSize => Math.Clamp(PageSize, 1, MaxPageSize);

    public string? SearchTerm => string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
}

/// <summary>
/// Whitelist of sortable columns for one entity. Client-supplied <c>sortBy</c> values are looked up here and never
/// reach the query directly, so unknown or sensitive columns cannot be requested.
/// A stable tie-breaker on <see cref="Entity.Id"/> keeps page boundaries deterministic.
/// </summary>
public sealed class SortMap<TEntity>
    where TEntity : Entity
{
    private readonly Dictionary<string, Func<IQueryable<TEntity>, bool, IOrderedQueryable<TEntity>>> _sorts =
        new(StringComparer.OrdinalIgnoreCase);

    private string? _defaultKey;
    private bool _defaultDescending;

    public SortMap<TEntity> Add<TKey>(string name, Expression<Func<TEntity, TKey>> keySelector)
    {
        _sorts[name] = (query, descending) => descending
            ? query.OrderByDescending(keySelector).ThenByDescending(e => e.Id)
            : query.OrderBy(keySelector).ThenBy(e => e.Id);
        return this;
    }

    public SortMap<TEntity> Default(string name, bool descending)
    {
        _defaultKey = name;
        _defaultDescending = descending;
        return this;
    }

    public IQueryable<TEntity> Apply(IQueryable<TEntity> query, string? sortBy, SortDirection? direction)
    {
        var key = sortBy is not null && _sorts.ContainsKey(sortBy) ? sortBy : _defaultKey;
        if (key is null)
        {
            return query.OrderBy(e => e.Id);
        }

        var descending = sortBy is not null && _sorts.ContainsKey(sortBy)
            ? direction == SortDirection.Desc
            : _defaultDescending;

        return _sorts[key](query, descending);
    }
}

public static class PagingExtensions
{
    /// <summary>Counts, then fetches one page of the (already ordered) query, projecting to <typeparamref name="TResult"/>.</summary>
    public static async Task<PagedResponse<TResult>> ToPagedResponseAsync<TSource, TResult>(
        this IQueryable<TSource> ordered,
        Expression<Func<TSource, TResult>> projection,
        PagedQuery paging,
        CancellationToken cancellationToken)
    {
        var page = paging.SafePage;
        var pageSize = paging.SafePageSize;

        var total = await ordered.CountAsync(cancellationToken);
        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(projection)
            .ToListAsync(cancellationToken);

        return PagedResponse<TResult>.Create(items, page, pageSize, total);
    }

    /// <summary>Escapes LIKE wildcards so user input is matched literally.</summary>
    public static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal);
}
