namespace TeamFlow.Api.Http;

public static class Input
{
    public static string Required(string? value, string field, int maxLength)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > maxLength)
            throw new ApiException(400, "invalid_input", $"{field} is required and must contain at most {maxLength} characters.");
        return text;
    }
}
