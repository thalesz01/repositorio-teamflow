namespace TeamFlow.Api;

public sealed class ServerOptions
{
    public const string SectionName = "Server";

    public string Url { get; init; } = string.Empty;

    public static bool IsValidUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && !string.IsNullOrWhiteSpace(uri.Host)
        && uri.AbsolutePath == "/"
        && uri.Query.Length == 0
        && uri.Fragment.Length == 0
        && uri.UserInfo.Length == 0;
}
