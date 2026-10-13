using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Integrations;

public interface IGitProvider
{
    Task<string> VerifyAsync(GitConnection connection, string privateKey, CancellationToken ct);
    Task<InstallationToken> TokenAsync(GitConnection connection, string privateKey, CancellationToken ct);
}

public sealed class GitHubProvider(IHttpClientFactory clients, TimeProvider clock) : IGitProvider
{
    public async Task<string> VerifyAsync(GitConnection connection, string privateKey, CancellationToken ct)
    {
        var token = await TokenAsync(connection, privateKey, ct);
        using var repository = await SendAsync(HttpMethod.Get, $"repos/{connection.Owner}/{connection.Repository}", token.Token, null, ct);
        var repo = repository.RootElement;
        if (repo.GetProperty("id").GetInt64() != connection.RepositoryId
            || !string.Equals(repo.GetProperty("full_name").GetString(), $"{connection.Owner}/{connection.Repository}", StringComparison.OrdinalIgnoreCase))
            throw new ApiException(409, "repository_mismatch", "Repository identity differs from the authorized connection.");
        using var commit = await SendAsync(HttpMethod.Get, $"repos/{connection.Owner}/{connection.Repository}/git/ref/heads/{Uri.EscapeDataString(connection.BaseBranch)}", token.Token, null, ct);
        var sha = commit.RootElement.GetProperty("object").GetProperty("sha").GetString() ?? "";
        if (!GitRules.IsCommit(sha)) throw new ApiException(502, "git_invalid_response", "Git provider returned an invalid commit.");
        return sha;
    }

    public Task<InstallationToken> TokenAsync(GitConnection connection, string privateKey, CancellationToken ct)
        => CreateTokenAsync(connection, privateKey, false, ct);

    public Task<InstallationToken> PublicationTokenAsync(GitConnection connection, string privateKey, CancellationToken ct)
        => CreateTokenAsync(connection, privateKey, true, ct);

    public Task<InstallationToken> ReviewTokenAsync(GitConnection connection, string privateKey, CancellationToken ct)
        => CreateTokenAsync(connection, privateKey, false, ct, review: true);

    private async Task<InstallationToken> CreateTokenAsync(GitConnection connection, string privateKey, bool publication, CancellationToken ct, bool review = false)
    {
        string jwt;
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKey);
            if (rsa.KeySize < 2048) throw new CryptographicException("Unsupported key size.");
            var now = clock.GetUtcNow().ToUnixTimeSeconds();
            var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
            var payload = Encode(JsonSerializer.SerializeToUtf8Bytes(new { iat = now - 60, exp = now + 540, iss = connection.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
            var message = header + "." + payload;
            jwt = message + "." + Encode(rsa.SignData(Encoding.UTF8.GetBytes(message), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }
        catch (Exception error) when (error is ArgumentException or CryptographicException)
        { throw new ApiException(503, "git_invalid_configuration", "GitHub App signing configuration is invalid."); }
        using var response = await SendAsync(HttpMethod.Post, $"app/installations/{connection.InstallationId}/access_tokens", jwt,
            publication
                ? new { repository_ids = new[] { connection.RepositoryId }, permissions = (object)new { contents = "write", metadata = "read", pull_requests = "write" } }
                : review ? new { repository_ids = new[] { connection.RepositoryId }, permissions = (object)new { contents = "read", metadata = "read", pull_requests = "read", checks = "read" } }
                : new { repository_ids = new[] { connection.RepositoryId }, permissions = (object)new { contents = "read", metadata = "read" } }, ct);
        var root = response.RootElement;
        var value = root.GetProperty("token").GetString() ?? "";
        var expiry = root.GetProperty("expires_at").GetDateTimeOffset();
        if (value.Length is < 8 or > 8192 || value.Any(char.IsWhiteSpace) || expiry <= clock.GetUtcNow().AddSeconds(30))
            throw new ApiException(502, "git_invalid_response", "Git provider returned an invalid installation token.");
        return new(value, expiry);
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, string token, object? body, CancellationToken ct)
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
            if (!response.IsSuccessStatusCode) throw new ApiException(502, "git_access_denied", "GitHub access failed; check installation permissions and revocation.");
            return JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
        }
        catch (HttpRequestException) { throw new ApiException(502, "git_unavailable", "Git provider is unavailable."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ApiException(504, "git_timeout", "Git provider request timed out."); }
        catch (JsonException) { throw new ApiException(502, "git_invalid_response", "Git provider returned an unsupported response."); }
    }
    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
