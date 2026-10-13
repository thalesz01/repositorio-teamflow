using System.Xml.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using TeamFlow.Api.Data;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Auth;

public static class AuthConfiguration
{
    public static void AddTeamFlowAuth(this WebApplicationBuilder builder)
    {
        var local = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
        var keyPath = builder.Configuration["DataProtection:KeyPath"];
        if (!local && string.IsNullOrWhiteSpace(keyPath))
            throw new InvalidOperationException("DataProtection:KeyPath is required outside Development/Testing.");
        var protection = builder.Services.AddDataProtection().SetApplicationName("TeamFlow.Api");
        if (builder.Environment.IsEnvironment("Testing"))
            protection.UseEphemeralDataProtectionProvider();
        else
        {
            var certificatePath = builder.Configuration["DataProtection:CertificatePath"];
            var encrypted = OperatingSystem.IsWindows() || !string.IsNullOrWhiteSpace(certificatePath);
            if (!local && !encrypted) throw new InvalidOperationException("Encrypted Data Protection key storage is required outside Development/Testing.");
            var directory = new DirectoryInfo(keyPath ?? Path.Combine(builder.Environment.ContentRootPath, "work", encrypted ? "protected-keys" : "session-keys"));
            if (encrypted && directory.Exists)
                foreach (var file in directory.EnumerateFiles("key-*.xml"))
                    if (!XDocument.Load(file.FullName).Descendants().Attributes("decryptorType").Any())
                        throw new InvalidOperationException("Existing key storage contains unencrypted material. Configure a fresh protected directory or migrate existing keys first.");
            protection.PersistKeysToFileSystem(directory);
            if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
            else if (!string.IsNullOrWhiteSpace(certificatePath))
            {
                var certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, builder.Configuration["DataProtection:CertificatePassword"]);
                if (!certificate.HasPrivateKey) throw new InvalidOperationException("Data Protection certificate must include its private key.");
                builder.Services.AddSingleton(certificate);
                protection.ProtectKeysWithCertificate(certificate);
            }
        }

        builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        builder.Services.AddIdentityCore<AppUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 12;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<AppDbContext>().AddSignInManager();
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "TeamFlow.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = local ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
        });
        builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
        builder.Services.AddAuthorization();
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-Token";
            options.Cookie.Name = "TeamFlow.Csrf";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = local ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.AddPolicy("runner", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
    }

    public static void UseTeamFlowAuth(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method)
                && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method))
            {
                try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
                catch (AntiforgeryValidationException) { throw new ApiException(400, "invalid_csrf", "A valid CSRF token is required."); }
            }
            await next(context);
        });
    }
}
