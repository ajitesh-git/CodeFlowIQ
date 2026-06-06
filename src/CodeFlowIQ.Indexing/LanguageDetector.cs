using CodeFlowIQ.Core.Indexing;

namespace CodeFlowIQ.Indexing;

public sealed class LanguageDetector : ILanguageDetector
{
    private static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "csharp",
        [".cshtml"] = "razor",
        [".sql"] = "sql",
        [".tsql"] = "sql",
        [".js"] = "javascript",
        [".jsx"] = "javascript",
        [".ts"] = "typescript",
        [".tsx"] = "typescript",
        [".html"] = "html",
        [".css"] = "css",
        [".scss"] = "scss",
        [".json"] = "json",
        [".xml"] = "xml",
        [".yml"] = "yaml",
        [".yaml"] = "yaml",
        [".md"] = "markdown"
    };

    public string Detect(string filePath) =>
        Languages.TryGetValue(Path.GetExtension(filePath), out var language)
            ? language
            : "unknown";
}
