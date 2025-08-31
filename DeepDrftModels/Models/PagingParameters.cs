using System.Linq.Expressions;

namespace DeepDrftModels.Models;

public class PagingParameters
{
    private const int _maxPageSize = 100;
    private int _pageSize = 20;

    public int Page { get; set; } = 1;
    
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value > _maxPageSize ? _maxPageSize : value;
    }
}

public class PagingParameters<T> : PagingParameters
{
    public Expression<Func<T, object>>? OrderBy { get; set; }
    public bool IsDescending { get; set; } = false;

    public int Skip => (Page - 1) * PageSize;
}