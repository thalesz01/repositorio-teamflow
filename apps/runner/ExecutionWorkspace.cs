using System.Security.Cryptography;
using System.Text;

namespace TeamFlow.Runner;

public sealed class ExecutionWorkspace : IDisposable
{
    private readonly string _root;
    public string Path { get; }

    public ExecutionWorkspace(string root)
    {
        if (!System.IO.Path.IsPathFullyQualified(root)) throw new ArgumentException("Workspace root must be absolute.");
        _root = System.IO.Path.GetFullPath(root);
        RejectLinks(_root);
        Directory.CreateDirectory(_root);
        Path = System.IO.Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
        var owners = System.IO.Path.Combine(_root, ".owners");
        RejectLinks(owners);
        Directory.CreateDirectory(owners);
        File.WriteAllText(OwnershipPath(_root, Path), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(_root))));
    }

    private static string OwnershipPath(string root, string directory) =>
        System.IO.Path.Combine(root, ".owners", System.IO.Path.GetFileName(directory));

    private ExecutionWorkspace(string root, string existing)
    {
        _root = System.IO.Path.GetFullPath(root);
        Path = System.IO.Path.GetFullPath(existing);
        RequireChild(_root, Path);
        RejectLinks(_root);
    }

    public static void CleanupExpired(string root, DateTimeOffset now)
    {
        if (!System.IO.Path.IsPathFullyQualified(root)) throw new ArgumentException("Workspace root must be absolute.");
        root = System.IO.Path.GetFullPath(root);
        RejectLinks(root);
        if (!Directory.Exists(root)) return;
        RejectLinks(System.IO.Path.Combine(root, ".owners"));
        var ownership = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(root)));
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (!Guid.TryParseExact(System.IO.Path.GetFileName(directory), "N", out _)
                || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
            var marker = OwnershipPath(root, directory);
            if (!File.Exists(marker) || (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0
                || new FileInfo(marker).Length != ownership.Length
                || File.GetCreationTimeUtc(marker) > now.UtcDateTime.AddMinutes(-70)
                || File.ReadAllText(marker) != ownership) continue;
            using var expired = new ExecutionWorkspace(root, directory);
        }
    }

    public string Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || System.IO.Path.IsPathRooted(relativePath)
            || relativePath.Split('/', '\\').Any(part => part == "..") || relativePath.Contains(':'))
            throw new ArgumentException("Only relative paths without traversal are allowed.");
        var resolved = System.IO.Path.GetFullPath(System.IO.Path.Combine(Path, relativePath));
        RequireChild(Path, resolved);
        RejectLinks(resolved);
        return resolved;
    }

    private static void RequireChild(string root, string target)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!target.StartsWith(root.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException("Path escapes the execution workspace.");
    }

    private static void RejectLinks(string path)
    {
        for (var current = path; current is not null; current = System.IO.Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Links and reparse points are not allowed in workspace paths.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    public void Dispose()
    {
        RequireChild(_root, Path);
        RejectLinks(_root);
        if (Directory.Exists(Path)) DeleteOwnedDirectory(Path);
        var marker = OwnershipPath(_root, Path);
        RejectLinks(marker);
        if (File.Exists(marker)) File.Delete(marker);
    }

    private void DeleteOwnedDirectory(string directory)
    {
        RequireChild(_root, System.IO.Path.GetFullPath(directory));
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) { Directory.Delete(directory); return; }
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0) DeleteOwnedDirectory(entry);
            else
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) == 0 && (attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly);
                File.Delete(entry);
            }
        }
        var directoryAttributes = File.GetAttributes(directory);
        if ((directoryAttributes & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(directory, directoryAttributes & ~FileAttributes.ReadOnly);
        Directory.Delete(directory);
    }
}
