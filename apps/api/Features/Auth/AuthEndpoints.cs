using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Auth;

public sealed record Credentials(string Email, string Password);
public sealed record SessionDto(Guid UserId, string Email);
public sealed record CsrfDto(string Token);

public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/auth").WithTags("Auth");
        group.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return new CsrfDto(antiforgery.GetAndStoreTokens(context).RequestToken ?? throw new InvalidOperationException("CSRF token unavailable."));
        });
        group.MapPost("/register", RegisterAsync).RequireRateLimiting("auth").Produces<SessionDto>(201);
        group.MapPost("/login", LoginAsync).RequireRateLimiting("auth").Produces<SessionDto>();
        group.MapGet("/session", (ClaimsPrincipal user) => new SessionDto(UserId(user), user.FindFirstValue(ClaimTypes.Email) ?? ""))
            .RequireAuthorization();
        group.MapPost("/logout", async (SignInManager<AppUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization();
    }

    public static Guid UserId(ClaimsPrincipal user) => Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw new ApiException(401, "unauthenticated", "Authentication is required.");

    private static async Task<IResult> RegisterAsync(Credentials request, UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        var email = Input.Required(request.Email, "email", 254);
        if (!MailAddress.TryCreate(email, out var address) || address.Address != email)
            throw new ApiException(400, "invalid_email", "Enter a valid email address.");
        if (request.Password is null || request.Password.Length is < 12 or > 128)
            throw new ApiException(400, "invalid_password", "Password must contain 12..128 characters.");
        var user = new AppUser { Id = Guid.NewGuid(), UserName = email, Email = email };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            throw new ApiException(400, "registration_failed", "Unable to register. Check email and password requirements.");
        await signIn.SignInAsync(user, isPersistent: false);
        return Results.Created("/api/v1/auth/session", new SessionDto(user.Id, email));
    }

    private static async Task<IResult> LoginAsync(Credentials request, UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        var email = Input.Required(request.Email, "email", 254);
        if (request.Password is null || request.Password.Length > 128)
            throw new ApiException(401, "invalid_credentials", "Invalid credentials.");
        var user = await users.FindByEmailAsync(email);
        var result = user is null ? SignInResult.Failed : await signIn.PasswordSignInAsync(user, request.Password, false, true);
        if (user is null || !result.Succeeded)
            throw new ApiException(401, "invalid_credentials", "Invalid credentials.");
        return Results.Ok(new SessionDto(user.Id, user.Email ?? email));
    }
}
