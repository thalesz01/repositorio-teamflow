using Microsoft.AspNetCore.DataProtection;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Integrations;

public sealed class IntegrationSecrets(IDataProtectionProvider protection, IConfiguration configuration, IHostEnvironment environment)
{
    public string Configured(Guid organizationId, Guid projectId, string key) =>
        configuration[$"Integrations:{organizationId:D}:{projectId:D}:{key}"]
        ?? throw new ApiException(503, "integration_not_configured", "Configure the project integration on the server first.");

    public string Protect(Guid organizationId, Guid projectId, Guid id, string purpose, string value)
    {
        if (!environment.IsEnvironment("Testing") && !OperatingSystem.IsWindows()
            && string.IsNullOrWhiteSpace(configuration["DataProtection:CertificatePath"]))
            throw new ApiException(503, "secret_protection_required", "Encrypted key storage is required before accepting integration secrets.");
        return Protector(organizationId, projectId, id, purpose).Protect(value);
    }

    public string Unprotect(Guid organizationId, Guid projectId, Guid id, string purpose, string value)
    {
        try { return Protector(organizationId, projectId, id, purpose).Unprotect(value); }
        catch (System.Security.Cryptography.CryptographicException)
        { throw new ApiException(503, "secret_unavailable", "Integration credentials cannot be decrypted; verify key storage and scope."); }
    }

    private IDataProtector Protector(Guid organizationId, Guid projectId, Guid id, string purpose) =>
        protection.CreateProtector("TeamFlow.Integrations", organizationId.ToString("D"), projectId.ToString("D"), id.ToString("D"), purpose);
}
