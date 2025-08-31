namespace DeepDrftModels.Models;

public class PagedResult<T>
{
    public IEnumerable<T> Items { get; set; } = new List<T>();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;

    public PagedResult()
    {
    }

    public static PagedResult<T> From<TOther>(PagedResult<TOther> other, IEnumerable<T> items)
    {
        return new PagedResult<T>()
        {
            Items = items.ToList(),
            Page = other.Page,
            PageSize = other.PageSize,
            TotalCount = other.TotalCount,
        };
    }

    public PagedResult(IEnumerable<T> items, int totalCount, int page, int pageSize)
    {
        Items = items.ToList() ?? new List<T>();
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }
} 