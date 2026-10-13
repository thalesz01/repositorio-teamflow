using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TeamFlow.Api.Data;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Integrations;

public sealed record CodexMachineStatus(bool Installed, bool Authenticated, bool Enabled);

public sealed class CodexMachine(IConfiguration configuration, AppDbContext db)
{
    public async Task<CodexMachineStatus> StatusAsync(CancellationToken ct)
    {
        var executable = configuration["Codex:Executable"] ?? "";
        var installed = Path.IsPathFullyQualified(executable) && File.Exists(executable) && Path.GetExtension(executable).Equals(".exe", StringComparison.OrdinalIgnoreCase);
        var enabled = configuration.GetValue<bool>("Codex:EnableRequests");
        if (!installed) return new(false, false, enabled);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        start.ArgumentList.Add("login"); start.ArgumentList.Add("status");
        start.Environment.Clear();
        foreach (var name in new[] { "PATH", "SystemRoot", "TEMP", "TMP", "USERPROFILE", "HOME", "APPDATA", "LOCALAPPDATA", "CODEX_HOME" })
            if (Environment.GetEnvironmentVariable(name) is { } value) start.Environment[name] = value;
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot inspect Codex.");
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            try { await process.WaitForExitAsync(timeout.Token); }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); }
            await output; await error;
            return new(true, process.ExitCode == 0, enabled);
        }
        catch (Exception failure) when (failure is System.ComponentModel.Win32Exception or IOException or OperationCanceledException)
        { ct.ThrowIfCancellationRequested(); return new(true, false, enabled); }
    }

    public AgentProfile ApplySettings(AgentProfile profile)
    {
        profile.Model = configuration["Codex:Model"] ?? "default";
        profile.MaxInputBytes = configuration.GetValue("Codex:MaxInputBytes", 32768);
        profile.MaxOutputTokens = 8192;
        profile.TimeoutSeconds = configuration.GetValue("Codex:TimeoutSeconds", 120);
        return profile;
    }

    // Scoped records preserve historical execution/memory foreign keys; settings and credentials belong to the machine.
    public async Task<AgentProfile> EnsureBindingAsync(Guid organizationId, Guid projectId, CancellationToken ct)
    {
        var status = await StatusAsync(ct);
        if (!status.Installed || !status.Authenticated || !status.Enabled)
            throw new ApiException(503, "codex_machine_unavailable", "Authenticate and enable Codex on the machine running TeamFlow.");
        var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"codex-machine:{organizationId:D}:{projectId:D}"))[..16]);
        var existing = await db.AgentProfiles.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (existing is not null) { ApplySettings(existing); existing.RevokedAt = null; await db.SaveChangesAsync(ct); return existing; }
        var profile = ApplySettings(new() { Id = id, OrganizationId = organizationId, ProjectId = projectId, Provider = "codex", Name = "Codex da máquina" });
        db.AgentProfiles.Add(profile);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException failure) when (failure.InnerException is PostgresException { SqlState: "23505" })
        { db.Entry(profile).State = EntityState.Detached; return await db.AgentProfiles.SingleAsync(p => p.Id == id && p.OrganizationId == organizationId && p.ProjectId == projectId, ct); }
        return profile;
    }
}
