using System.Net.Http.Headers;
using System.Text.Json;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Integrations;

public sealed record CiPolicy(long AppId, string[] RequiredChecks);
public sealed record GitReviewSnapshot(string HeadCommit, string BaseCommit, bool CiPassed,
    bool Merged, string? MergeCommit, string? MergeBaseParent);
public interface IGitReview
{
    Task<GitReviewSnapshot> ReadAsync(GitConnection connection, string privateKey, long number, string branch, CiPolicy policy, CancellationToken ct);
}

public sealed class GitHubReview(IHttpClientFactory clients, GitHubProvider tokens) : IGitReview
{
    public async Task<GitReviewSnapshot> ReadAsync(GitConnection connection, string privateKey, long number, string branch, CiPolicy policy, CancellationToken ct)
    {
        if (number < 1 || policy.AppId < 1 || policy.RequiredChecks.Length is < 1 or > 20
            || policy.RequiredChecks.Any(n => string.IsNullOrWhiteSpace(n) || n.Length > 160 || n.Any(char.IsControl)))
            throw new ApiException(503, "ci_policy_required", "Configure the trusted CI App and required checks.");
        var grant = await tokens.ReviewTokenAsync(connection, privateKey, ct);
        var repo = $"repos/{connection.Owner}/{connection.Repository}";
        using var pr = await ReadAsync($"{repo}/pulls/{number}", grant.Token, ct);
        var root = pr.RootElement;
        if (root.GetProperty("number").GetInt64() != number
            || root.GetProperty("base").GetProperty("repo").GetProperty("id").GetInt64() != connection.RepositoryId
            || root.GetProperty("head").GetProperty("repo").GetProperty("id").GetInt64() != connection.RepositoryId
            || root.GetProperty("head").GetProperty("ref").GetString() != branch
            || root.GetProperty("base").GetProperty("ref").GetString() != connection.BaseBranch)
            throw new ApiException(409, "pull_request_conflict", "PR identity differs from the registered publication.");
        var head = Commit(root.GetProperty("head").GetProperty("sha"));
        var baseCommit = Commit(root.GetProperty("base").GetProperty("sha"));
        using var runs = await ReadAsync($"{repo}/commits/{head}/check-runs?filter=latest&per_page=100", grant.Token, ct);
        if (runs.RootElement.GetProperty("total_count").GetInt32() > 100)
            throw new ApiException(409, "ci_check_limit", "CI evidence exceeds the supported limit.");
        var checks = runs.RootElement.GetProperty("check_runs").EnumerateArray()
            .Where(r => r.GetProperty("app").GetProperty("id").GetInt64() == policy.AppId && r.GetProperty("head_sha").GetString() == head).ToArray();
        var passed = policy.RequiredChecks.All(name =>
        {
            var latest = checks.Where(r => r.GetProperty("name").GetString() == name)
                .OrderByDescending(r => r.GetProperty("id").GetInt64()).FirstOrDefault();
            return latest.ValueKind == JsonValueKind.Object && latest.GetProperty("status").GetString() == "completed"
                && latest.GetProperty("conclusion").GetString() == "success";
        });
        var merged = root.GetProperty("merged").GetBoolean();
        string? merge = null;
        string? parent = null;
        if (merged)
        {
            merge = Commit(root.GetProperty("merge_commit_sha"));
            using var commit = await ReadAsync($"{repo}/git/commits/{merge}", grant.Token, ct);
            var parents = commit.RootElement.GetProperty("parents");
            if (parents.GetArrayLength() > 0) parent = Commit(parents[0].GetProperty("sha"));
        }
        return new(head, baseCommit, passed, merged, merge, parent);
    }

    private async Task<JsonDocument> ReadAsync(string path, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        request.Headers.UserAgent.ParseAdd("TeamFlow/0.1");
        try
        {
            using var response = await clients.CreateClient("GitHub").SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) throw new ApiException(502, "git_access_denied", "GitHub review query failed.");
            return JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
        }
        catch (HttpRequestException) { throw new ApiException(502, "git_unavailable", "Git provider is unavailable."); }
        catch (JsonException) { throw new ApiException(502, "git_invalid_response", "Invalid review response."); }
    }
    private static string Commit(JsonElement value)
    {
        var sha = value.GetString() ?? "";
        return GitRules.IsCommit(sha) ? sha : throw new ApiException(502, "git_invalid_response", "Invalid commit identity.");
    }
}
