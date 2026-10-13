namespace TeamFlow.Api.Http;

public sealed record PageResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public static class Pagination
{
    public static (int Page, int PageSize) Validate(int? page, int? pageSize)
    {
        var values = (Page: page ?? 1, PageSize: pageSize ?? 20);
        if (values.Page is < 1 or > 100000 || values.PageSize is < 1 or > 100)
            throw new ApiException(400, "invalid_pagination", "Page must be 1..100000 and pageSize 1..100.");
        return values;
    }
}
