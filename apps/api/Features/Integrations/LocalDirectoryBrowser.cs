using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Integrations;

public sealed record LocalDirectoryEntry(string Name, string Path);
public sealed record LocalDirectoryListing(string? CurrentPath, string? ParentPath, string[] Roots,
    LocalDirectoryEntry[] Directories, bool IsRepository, bool CanConnect, bool Truncated);

public static class LocalDirectoryBrowser
{
    public static LocalDirectoryListing Browse(IConfiguration configuration, Guid organizationId, Guid projectId, string? requested)
    {
        var section = Settings(configuration, organizationId, projectId);
        var roots = Roots(section);
        if (requested is null) return new(null, null, roots, [], false, false, false);
        var path = Normalize(requested);
        if (!roots.Any(root => Contains(root, path)))
            throw new ApiException(403, "repository_not_authorized", "Directory is outside the project browsing roots.");
        try
        {
            for (DirectoryInfo? directory = new(path); directory is not null; directory = directory.Parent)
                if (!directory.Exists || directory.LinkTarget is not null)
                    throw new ApiException(400, "directory_unavailable", "Directory is unavailable or is a link.");
            var entries = new DirectoryInfo(path).EnumerateDirectories()
                .Where(directory => directory.LinkTarget is null && !directory.Name.StartsWith('.')
                    && !directory.Attributes.HasFlag(FileAttributes.Hidden))
                .Take(201).ToArray();
            var parent = Directory.GetParent(path)?.FullName;
            var git = new DirectoryInfo(Path.Combine(path, ".git"));
            var isRepository = git.Exists && git.LinkTarget is null;
            return new(path, parent is not null && roots.Any(root => Contains(root, parent)) ? parent : null, roots,
                entries.Take(200).OrderBy(directory => directory.Name, PathComparer)
                    .Select(directory => new LocalDirectoryEntry(directory.Name, directory.FullName)).ToArray(),
                isRepository, isRepository && CanSelectRepository(configuration, organizationId, projectId, path), entries.Length > 200);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        { throw new ApiException(400, "directory_unavailable", "Directory cannot be listed."); }
    }

    public static bool CanSelectRepository(IConfiguration configuration, Guid organizationId, Guid projectId, string requested)
    {
        var section = Settings(configuration, organizationId, projectId);
        var path = Normalize(requested);
        if (section.GetValue<bool>("AllowRepositorySelection")) return Roots(section).Any(root => Contains(root, path));
        var repository = section["RepositoryPath"]
            ?? throw new ApiException(503, "integration_not_configured", "Configure a local repository for this project.");
        return PathComparer.Equals(path, Normalize(repository));
    }

    // A project's own section wins; without one, the server-wide "Local" section applies (folders are chosen per task).
    private static IConfigurationSection Settings(IConfiguration configuration, Guid organizationId, Guid projectId)
    {
        var project = configuration.GetSection($"Integrations:{organizationId:D}:{projectId:D}:Local");
        return project.Exists() ? project : configuration.GetSection("Local");
    }

    private static string[] Roots(IConfigurationSection section)
    {
        var configured = section.GetValue<bool>("BrowseAllDrives")
            ? DriveInfo.GetDrives().Where(drive => drive.DriveType is DriveType.Fixed or DriveType.Removable && drive.IsReady)
                .Select(drive => drive.RootDirectory.FullName).ToArray()
            : section.GetSection("BrowseRoots").Get<string[]>() ?? (section["RepositoryPath"] is { } repository ? [repository] : []);
        if (configured.Length == 0)
            throw new ApiException(503, "integration_not_configured", "Configure local directory roots for this project.");
        return configured.Select(Normalize).Distinct(PathComparer).ToArray();
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static bool Contains(string root, string path) => PathComparer.Equals(root, path)
        || path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 || !Path.IsPathFullyQualified(path))
            throw new ApiException(400, "invalid_repository", "An absolute directory path is required.");
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (Exception failure) when (failure is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new ApiException(400, "invalid_repository", "Directory path is invalid."); }
    }
}
