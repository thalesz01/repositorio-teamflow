using System.Text.RegularExpressions;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Integrations;

public static partial class GitRules
{
    public static void Validate(GitInput input)
    {
        if (input.InstallationId <= 0 || input.RepositoryId <= 0 || !Name().IsMatch(input.Owner ?? "")
            || !Name().IsMatch(input.Repository ?? "") || input.Owner is "." or ".." || input.Repository is "." or ".."
            || input.BaseBranch is null || !Branch().IsMatch(input.BaseBranch) || input.BaseBranch.Contains("..", StringComparison.Ordinal)
            || input.BaseBranch.Split('/').Any(part => part.Length == 0 || part.StartsWith('.') || part.EndsWith('.') || part.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)))
            throw new ApiException(400, "invalid_repository", "Invalid GitHub repository identity or base branch.");
    }
    public static string Url(GitConnection connection) => connection.Provider == "local" ? connection.LocalPath : $"https://github.com/{connection.Owner}/{connection.Repository}.git";
    public static bool IsCommit(string commit) => Commit().IsMatch(commit);
    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,99}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Name();
    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9/_.-]{0,119}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Branch();
    [GeneratedRegex(@"^[a-f0-9]{40}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Commit();
}
