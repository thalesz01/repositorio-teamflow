using System.Text;
using System.Text.RegularExpressions;

namespace TeamFlow.Api.Features.Executions;

// Same glob rules the runner applies to controller policy: * and ? stay inside a folder, ** crosses folders.
public static class ToolPathPattern
{
    public static bool IsMatch(string pattern, string path)
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
        return Regex.IsMatch(path, regex.Append(@"\z").ToString(), RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }
}
