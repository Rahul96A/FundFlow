namespace FundFlow.Contracts.Common;

/// <summary>Standard envelope for every list endpoint.</summary>
public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages)
{
    public static PagedResponse<T> Create(IReadOnlyList<T> items, int page, int pageSize, int totalCount) =>
        new(items, page, pageSize, totalCount, pageSize <= 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize));
}
