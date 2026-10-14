using System.Text.RegularExpressions;

namespace TeamFlow.Runner;

public static partial class TestOutput
{
    public static string Sanitize(string output)
    {
        var safe = Secrets().Replace(output, "[redacted]");
        return safe.Length <= 3000 ? safe : safe[..3000] + "\n[diagnostic shortened]";
    }

    [GeneratedRegex(@"(?is)-----BEGIN [A-Z ]*PRIVATE KEY-----.*?-----END [A-Z ]*PRIVATE KEY-----|\b(?:sk|ghp|github_pat)[-_][A-Za-z0-9_-]{16,}|\bBearer\s+[A-Za-z0-9._~-]{16,}|\b(?:password|api[_-]?key|secret|access[_-]?token)[""']?\s*[:=]\s*[""']?[^\s<*""']{4,}")]
    private static partial Regex Secrets();
}
