namespace Retail.Api.Common.Models;

/// <summary>
/// A single page of results plus the paging metadata the client needs to render
/// pagination controls. Wrapped in <see cref="ApiResponse{T}"/> like any payload.
/// </summary>
/// <typeparam name="T">The item type (typically a response DTO).</typeparam>
public sealed class PagedResult<T>
{
    /// <summary>The items on this page.</summary>
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>1-based page number.</summary>
    public int Page { get; init; }

    /// <summary>Requested page size.</summary>
    public int PageSize { get; init; }

    /// <summary>Total items across all pages (before paging).</summary>
    public int TotalCount { get; init; }

    /// <summary>Total number of pages for the current page size.</summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;

    /// <summary><c>true</c> if there is a page after this one.</summary>
    public bool HasNext => Page < TotalPages;

    /// <summary><c>true</c> if there is a page before this one.</summary>
    public bool HasPrevious => Page > 1;

    /// <summary>Parameterless ctor for serialization.</summary>
    public PagedResult()
    {
    }

    /// <summary>Builds a page from its items and the total count.</summary>
    public PagedResult(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }
}
