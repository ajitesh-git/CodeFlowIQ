using CodeFlowIQ.Core.Query;

namespace CodeFlowIQ.Indexing;

internal static class WorkspaceQueryText
{
    public static string FormatLanguageTitle(string languageId) =>
        languageId switch
        {
            "csharp" => "C# / ASP.NET",
            "sql" => "SQL / T-SQL",
            "typescript" => "TypeScript",
            "javascript" => "JavaScript",
            "html" => "HTML / Angular Templates",
            "json" => "JSON configuration",
            _ => languageId
        };

    public static string NormalizeFlowName(string value)
    {
        var cleaned = ExtractMetadataValue(value, "backendApi")
            ?? ExtractMetadataValue(value, "frontendApi")
            ?? value;

        var methodSeparator = cleaned.IndexOf(' ', StringComparison.Ordinal);
        if (methodSeparator >= 0 && methodSeparator < cleaned.Length - 1)
        {
            cleaned = cleaned[(methodSeparator + 1)..];
        }

        cleaned = cleaned
            .Trim('/')
            .Replace("{", string.Empty, StringComparison.Ordinal)
            .Replace("}", string.Empty, StringComparison.Ordinal)
            .Replace("_", " ", StringComparison.Ordinal)
            .Replace("-", " ", StringComparison.Ordinal)
            .Replace("/", " / ", StringComparison.Ordinal);

        return string.IsNullOrWhiteSpace(cleaned)
            ? "Detected flow"
            : CapitalizeWords(cleaned);
    }

    public static string? ExtractMetadataValue(string metadata, string key)
    {
        foreach (var part in metadata.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = part.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                continue;
            }

            var partKey = part[..separatorIndex];
            if (partKey.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return part[(separatorIndex + 1)..];
            }
        }

        return null;
    }

    public static string FormatIdentifier(string identifier)
    {
        var cleaned = identifier.Split(['\\', '/']).LastOrDefault() ?? identifier;
        var qualifiedIndex = cleaned.LastIndexOf("::", StringComparison.Ordinal);
        if (qualifiedIndex >= 0 && qualifiedIndex < cleaned.Length - 2)
        {
            cleaned = cleaned[(qualifiedIndex + 2)..];
        }

        return cleaned
            .Replace(".cs", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(".ts", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(".sql", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static string CapitalizeWords(string value) =>
        string.Join(' ', value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Length == 0 ? x : $"{char.ToUpperInvariant(x[0])}{x[1..]}"));
}
