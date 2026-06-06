using System.IO.Enumeration;
using CodeFlowIQ.Core.Indexing;

namespace CodeFlowIQ.Indexing;

public sealed class IgnoreRuleSet
{
    private readonly List<string> _rules;

    private IgnoreRuleSet(IEnumerable<string> rules)
    {
        _rules = rules
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith('#'))
            .Select(NormalizeRule)
            .ToList();
    }

    public static IgnoreRuleSet Load(string rootPath, IndexingOptions options)
    {
        var ignorePath = Path.Combine(rootPath, options.IgnoreFileName);
        return File.Exists(ignorePath)
            ? new IgnoreRuleSet(File.ReadAllLines(ignorePath))
            : new IgnoreRuleSet([]);
    }

    public bool IsIgnored(string relativePath, bool isDirectory)
    {
        var normalizedPath = NormalizePath(relativePath);
        var fileOrDirectoryName = Path.GetFileName(normalizedPath);

        foreach (var rule in _rules)
        {
            if (isDirectory && rule.EndsWith('/') && IsMatch(rule.TrimEnd('/'), normalizedPath, fileOrDirectoryName))
            {
                return true;
            }

            if (!rule.EndsWith('/') && IsMatch(rule, normalizedPath, fileOrDirectoryName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMatch(string rule, string normalizedPath, string fileOrDirectoryName)
    {
        if (rule.StartsWith('/'))
        {
            return FileSystemName.MatchesSimpleExpression(rule.TrimStart('/'), normalizedPath, ignoreCase: true);
        }

        return FileSystemName.MatchesSimpleExpression(rule, normalizedPath, ignoreCase: true)
            || FileSystemName.MatchesSimpleExpression(rule, fileOrDirectoryName, ignoreCase: true)
            || normalizedPath.StartsWith(rule + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRule(string rule) => NormalizePath(rule.Trim());

    private static string NormalizePath(string path) => path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
}
