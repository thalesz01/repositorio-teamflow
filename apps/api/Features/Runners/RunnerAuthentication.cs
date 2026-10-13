using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TeamFlow.Api.Data;

namespace TeamFlow.Api.Features.Runners;

public sealed class RunnerAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, AppDbContext db, TimeProvider clock, IHostEnvironment environment)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Runner";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var local = environment.IsDevelopment() || environment.IsEnvironment("Testing");
        var loopback = Request.Host.Host is "localhost" or "127.0.0.1" or "::1";
        var remote = Context.Connection.RemoteIpAddress;
        if (!Request.IsHttps && !(local && loopback && (remote is null || IPAddress.IsLoopback(remote))))
            return AuthenticateResult.Fail("Runner authentication requires HTTPS.");
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || header.Length > 150)
            return AuthenticateResult.NoResult();
        var token = header[7..];
        var parts = token.Split('.');
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out var id) || parts[1].Length != 64)
            return AuthenticateResult.Fail("Invalid runner credential.");
        var credential = await db.RunnerCredentials.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, Context.RequestAborted);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        if (credential is null || credential.RevokedAt is not null || credential.ExpiresAt <= clock.GetUtcNow()
            || !CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(credential.TokenHash)))
            return AuthenticateResult.Fail("Invalid runner credential.");
        var identity = new ClaimsIdentity(new[] {
            new Claim("runnerId", credential.Id.ToString()), new Claim("workerId", credential.WorkerId.ToString()),
            new Claim("organizationId", credential.OrganizationId.ToString()), new Claim("projectId", credential.ProjectId.ToString())
        }, SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
