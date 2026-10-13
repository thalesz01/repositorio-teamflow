using System.Text.RegularExpressions;

namespace TeamFlow.Api.Features.Integrations;

public static partial class GitRules
{
    public static string Url(GitConnection connection) => connection.LocalPath;
    public static bool IsCommit(string commit) => Commit().IsMatch(commit);
    [GeneratedRegex(@"^[a-f0-9]{40}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Commit();
}
