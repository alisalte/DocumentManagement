namespace Dms.Application;

/// <summary>One page of a list. <see cref="Total"/> counts every row the caller may see.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public const int MaxPageSize = 100;

    /// <summary>Clamps client input so a request cannot ask for page 0 or ten thousand rows.</summary>
    public static (int Page, int PageSize) Normalize(int? page, int? pageSize) =>
        (Math.Max(page ?? 1, 1), Math.Clamp(pageSize ?? 25, 1, MaxPageSize));
}
