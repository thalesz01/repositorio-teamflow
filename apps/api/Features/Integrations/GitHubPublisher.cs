using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Integrations;

public sealed record PublicationFile(string Path, string Content, string Hash);
public sealed record PublicationPlan(Guid ExecutionId, string BaseCommit, string Branch, string Title,
    string Body, DateTimeOffset CreatedAt, PublicationFile[] Files);
public sealed record PublishedPullRequest(long Number, string Url, string BaseCommit, string HeadCommit, string Branch);

public interface IGitPublisher
{
    Task<PublishedPullRequest> PublishAsync(GitConnection connection, string privateKey, PublicationPlan plan, CancellationToken ct);
}

// Only server-verified evidence may enter this boundary. Provider receipts are checked on every replay.
public sealed class GitHubPublisher(IHttpClientFactory clients, GitHubProvider tokens) : IGitPublisher
{
    public async Task<PublishedPullRequest> PublishAsync(GitConnection connection, string privateKey, PublicationPlan plan, CancellationToken ct)
    {
        Validate(connection, plan);
        var grant = await tokens.PublicationTokenAsync(connection, privateKey, ct);
        var repo = $"repos/{connection.Owner}/{connection.Repository}";
        using var identity = await SendAsync(HttpMethod.Get, repo, grant.Token, null, ct);
        if (identity is null || identity.RootElement.GetProperty("id").GetInt64() != connection.RepositoryId
            || !string.Equals(identity.RootElement.GetProperty("full_name").GetString(), $"{connection.Owner}/{connection.Repository}", StringComparison.OrdinalIgnoreCase))
            throw Conflict("repository_mismatch");
        using var currentBase = await SendAsync(HttpMethod.Get, $"{repo}/git/ref/heads/{Uri.EscapeDataString(connection.BaseBranch)}", grant.Token, null, ct);
        if (currentBase is null || Sha(currentBase.RootElement.GetProperty("object")) != plan.BaseCommit)
            throw Conflict("base_changed");
        using var parent = await SendAsync(HttpMethod.Get, $"{repo}/git/commits/{plan.BaseCommit}", grant.Token, null, ct);
        if (parent is null) throw Invalid();
        var baseTree = Sha(parent.RootElement.GetProperty("tree"));
        using var original = await SendAsync(HttpMethod.Get, $"{repo}/git/trees/{baseTree}?recursive=1", grant.Token, null, ct);
        if (original is null || original.RootElement.GetProperty("truncated").GetBoolean()) throw Conflict("repository_tree_limit");
        var entries = original.RootElement.GetProperty("tree").EnumerateArray().ToDictionary(e => e.GetProperty("path").GetString() ?? "", e => e.Clone(), StringComparer.Ordinal);
        var changes = plan.Files.OrderBy(f => f.Path, StringComparer.Ordinal).Select(file =>
        {
            var mode = "100644";
            if (entries.TryGetValue(file.Path, out var existing))
            {
                mode = existing.GetProperty("mode").GetString() ?? "";
                if (existing.GetProperty("type").GetString() != "blob" || mode is not ("100644" or "100755"))
                    throw Conflict("unsupported_file_mode");
            }
            foreach (var ancestor in Ancestors(file.Path))
                if (entries.TryGetValue(ancestor, out var entry) && entry.GetProperty("type").GetString() != "tree")
                    throw Conflict("unsupported_file_mode");
            return new { path = file.Path, mode, type = "blob", content = file.Content };
        }).ToArray();
        using var tree = await SendAsync(HttpMethod.Post, $"{repo}/git/trees", grant.Token, new { base_tree = baseTree, tree = changes }, ct);
        if (tree is null) throw Invalid();
        var treeSha = Sha(tree.RootElement);
        if (treeSha == baseTree) throw Conflict("no_changes");
        var marker = $"<!-- teamflow-execution:{plan.ExecutionId:D} -->";
        // A fixed date/message/tree/parent makes a retry create the same Git object.
        var author = new { name = "TeamFlow", email = "teamflow@users.noreply.github.com", date = plan.CreatedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture) };
        using var commit = await SendAsync(HttpMethod.Post, $"{repo}/git/commits", grant.Token,
            new { message = $"TeamFlow execution {plan.ExecutionId:D}", tree = treeSha, parents = new[] { plan.BaseCommit }, author, committer = author }, ct);
        if (commit is null) throw Invalid();
        var head = Sha(commit.RootElement);
        using var branch = await SendAsync(HttpMethod.Get, $"{repo}/git/ref/heads/{Uri.EscapeDataString(plan.Branch)}", grant.Token, null, ct, allowMissing: true);
        if (branch is not null)
        {
            if (Sha(branch.RootElement.GetProperty("object")) != head) throw Conflict("branch_conflict");
        }
        else
        {
            using var created = await SendAsync(HttpMethod.Post, $"{repo}/git/refs", grant.Token,
                new { @ref = "refs/heads/" + plan.Branch, sha = head }, ct, allowConflict: true);
            if (created is null)
            {
                using var reconciled = await SendAsync(HttpMethod.Get, $"{repo}/git/ref/heads/{Uri.EscapeDataString(plan.Branch)}", grant.Token, null, ct);
                if (reconciled is null || Sha(reconciled.RootElement.GetProperty("object")) != head) throw Conflict("branch_conflict");
            }
        }
        var existingPr = await FindAsync();
        if (existingPr is not null) return existingPr;
        using var opened = await SendAsync(HttpMethod.Post, $"{repo}/pulls", grant.Token,
            new { title = plan.Title, body = plan.Body + "\n\n" + marker, head = plan.Branch, @base = connection.BaseBranch, draft = true }, ct, allowConflict: true);
        if (opened is not null) return Receipt(opened.RootElement);
        return await FindAsync() ?? throw Conflict("publication_uncertain");

        async Task<PublishedPullRequest?> FindAsync()
        {
            using var found = await SendAsync(HttpMethod.Get, $"{repo}/pulls?state=all&head={Uri.EscapeDataString(connection.Owner + ":" + plan.Branch)}&base={Uri.EscapeDataString(connection.BaseBranch)}&per_page=100", grant.Token, null, ct);
            if (found is null || found.RootElement.ValueKind != JsonValueKind.Array) throw Invalid();
            var matches = found.RootElement.EnumerateArray().ToArray();
            if (matches.Length > 1) throw Conflict("pull_request_conflict");
            return matches.Length == 0 ? null : Receipt(matches[0]);
        }
        PublishedPullRequest Receipt(JsonElement pr)
        {
            var number = pr.GetProperty("number").GetInt64();
            var url = pr.GetProperty("html_url").GetString();
            var expectedUrl = $"https://github.com/{connection.Owner}/{connection.Repository}/pull/{number}";
            if (number < 1 || !string.Equals(url, expectedUrl, StringComparison.OrdinalIgnoreCase)
                || pr.GetProperty("head").GetProperty("sha").GetString() != head
                || pr.GetProperty("head").GetProperty("ref").GetString() != plan.Branch
                || pr.GetProperty("head").GetProperty("repo").GetProperty("id").GetInt64() != connection.RepositoryId
                || pr.GetProperty("base").GetProperty("sha").GetString() != plan.BaseCommit
                || pr.GetProperty("base").GetProperty("ref").GetString() != connection.BaseBranch
                || pr.GetProperty("base").GetProperty("repo").GetProperty("id").GetInt64() != connection.RepositoryId
                || !(pr.GetProperty("body").GetString() ?? "").Contains(marker, StringComparison.Ordinal))
                throw Conflict("pull_request_conflict");
            return new(number, expectedUrl, plan.BaseCommit, head, plan.Branch);
        }
    }

    private async Task<JsonDocument?> SendAsync(HttpMethod method, string path, string token, object? body,
        CancellationToken ct, bool allowMissing = false, bool allowConflict = false)
    {
        using var request = new HttpRequestMessage(method, "https://api.github.com/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        request.Headers.UserAgent.ParseAdd("TeamFlow/0.1");
        if (body is not null) request.Content = JsonContent.Create(body);
        try
        {
            using var response = await clients.CreateClient("GitHub").SendAsync(request, ct);
            if (allowMissing && response.StatusCode == HttpStatusCode.NotFound) return null;
            if (allowConflict && response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity) return null;
            if (!response.IsSuccessStatusCode) throw new ApiException(502, "git_access_denied", "GitHub publication failed; check installation permissions.");
            return JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
        }
        catch (HttpRequestException) { throw new ApiException(502, "git_unavailable", "Git provider is unavailable; reconcile before retrying."); }
        catch (JsonException) { throw Invalid(); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ApiException(504, "git_timeout", "Git provider request timed out; reconcile before retrying."); }
    }

    internal static void Validate(GitConnection connection, PublicationPlan plan)
    {
        if (connection.RevokedAt is not null || plan.ExecutionId == Guid.Empty || !GitRules.IsCommit(plan.BaseCommit)
            || plan.Branch != $"teamflow/{plan.ExecutionId:N}" || plan.Title.Length is < 1 or > 160 || plan.Title.Any(char.IsControl)
            || Encoding.UTF8.GetByteCount(plan.Body) > 32768 || plan.Files.Length is < 1 or > 100
            || plan.Files.Select(f => f.Path).Distinct(StringComparer.Ordinal).Count() != plan.Files.Length
            || plan.Files.Any(f => string.IsNullOrWhiteSpace(f.Path) || f.Path.Length > 240 || f.Path.Contains('\\') || f.Path.Contains(':')
                || f.Path.StartsWith('/') || f.Path.Split('/').Any(s => s.StartsWith('.') || s.Length == 0)
                || Encoding.UTF8.GetByteCount(f.Content) > 16384
                || f.Hash != Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(f.Content)))))
            throw new ApiException(400, "invalid_publication", "Publication plan violates the verified file contract.");
    }

    private static IEnumerable<string> Ancestors(string path)
    {
        for (var index = path.IndexOf('/'); index >= 0; index = path.IndexOf('/', index + 1)) yield return path[..index];
    }
    private static string Sha(JsonElement value)
    {
        var sha = value.GetProperty("sha").GetString() ?? "";
        return GitRules.IsCommit(sha) ? sha : throw Invalid();
    }
    private static ApiException Conflict(string code) => new(409, code, "Publication requires reconciliation or revalidation.");
    private static ApiException Invalid() => new(502, "git_invalid_response", "Git provider returned an unsupported publication response.");
}
