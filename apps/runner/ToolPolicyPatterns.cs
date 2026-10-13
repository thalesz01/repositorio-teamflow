using System.Text;
using System.Text.RegularExpressions;

namespace TeamFlow.Runner;

// Controller policy may use globs (*, ?, **). They are expanded once against the checked-out base, so the
// harness, the API and checkpoints keep working with an explicit, bounded file list. New files still need exact paths.
public static class ToolPolicyPatterns
{
    public const int MaxPaths = 100;
    private const int MaxScannedFiles = 20000;
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "node_modules" };

    public static RunnerToolPolicy Expand(RunnerToolPolicy policy, ExecutionWorkspace workspace)
    {
        var entries = policy.Readable.Concat(policy.Editable).ToArray();
        if (!entries.Any(IsPattern)) return policy;
        if (entries.Any(e => e.Contains("..") || Path.IsPathRooted(e) || e.Contains(':') || e.Contains('\\')))
            throw new ArgumentException("Tool policy patterns must be relative, use '/' and avoid traversal.");
        var files = Files(workspace.Path);
        return policy with { Readable = Expand(policy.Readable, files), Editable = Expand(policy.Editable, files) };
    }

    public static bool IsPattern(string entry) => entry.IndexOfAny(['*', '?']) >= 0;

    private static string[] Expand(string[] entries, IReadOnlyList<string> files)
    {
        var patterns = entries.Where(IsPattern).Select(ToRegex).ToArray();
        var expanded = entries.Where(e => !IsPattern(e))
            .Concat(files.Where(file => patterns.Any(p => p.IsMatch(file))))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (expanded.Length > MaxPaths)
            throw new ArgumentException($"Tool policy matches more than {MaxPaths} files; narrow the patterns.");
        return expanded;
    }

    private static Regex ToRegex(string pattern)
    {
        var regex = new StringBuilder(@"\A");
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                var slash = i + 2 < pattern.Length && pattern[i + 2] == '/';
                regex.Append(slash ? "(?:.*/)?" : ".*");
                i += slash ? 2 : 1;
            }
            else if (pattern[i] == '*') regex.Append("[^/]*");
            else if (pattern[i] == '?') regex.Append("[^/]");
            else regex.Append(Regex.Escape(pattern[i].ToString()));
        }
        return new Regex(regex.Append(@"\z").ToString(), RegexOptions.CultureInvariant);
    }

    // Hidden entries, links, build output and files above the tool size limit are never offered to the model.
    private static List<string> Files(string root)
    {
        var files = new List<string>();
        var pending = new Stack<string>([root]);
        while (pending.Count > 0)
        {
            foreach (var entry in new DirectoryInfo(pending.Pop()).EnumerateFileSystemInfos())
            {
                if (entry.Name.StartsWith('.') || (entry.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Hidden)) != 0) continue;
                if (entry is DirectoryInfo directory)
                {
                    if (!SkippedDirectories.Contains(directory.Name)) pending.Push(directory.FullName);
                    continue;
                }
                if (files.Count >= MaxScannedFiles) throw new ArgumentException("Workspace is too large to expand tool policy patterns.");
                if (((FileInfo)entry).Length <= WorkspaceTools.FileLimit)
                    files.Add(Path.GetRelativePath(root, entry.FullName).Replace('\\', '/'));
            }
        }
        files.Sort(StringComparer.Ordinal);
        return files;
    }
}
