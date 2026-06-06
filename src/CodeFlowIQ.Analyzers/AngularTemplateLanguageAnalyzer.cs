using System.Text.RegularExpressions;
using CodeFlowIQ.Core.Analysis;

namespace CodeFlowIQ.Analyzers;

public sealed class AngularTemplateLanguageAnalyzer : ILanguageAnalyzer
{
    private static readonly Regex AngularEventPattern = new(
        @"\((click|submit|ngSubmit|change|blur|focus)\)\s*=\s*[""']\s*([A-Za-z_$][\w$]*)\s*\(",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string LanguageId => "html";

    public bool CanAnalyze(string filePath) =>
        Path.GetExtension(filePath).Equals(".html", StringComparison.OrdinalIgnoreCase);

    public Task<CodeAnalysisResult> AnalyzeAsync(string filePath, string content, CancellationToken cancellationToken)
    {
        var symbols = new List<DiscoveredSymbol>();
        var relationships = new List<DiscoveredRelationship>();
        using var reader = new StringReader(content);
        var lineNumber = 0;

        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;

            foreach (Match match in AngularEventPattern.Matches(line))
            {
                var eventName = match.Groups[1].Value;
                var handlerName = match.Groups[2].Value;
                var symbolName = $"{eventName}:{handlerName}";

                symbols.Add(new DiscoveredSymbol(symbolName, "ui-event", null, lineNumber, match.Index + 1));
                relationships.Add(new DiscoveredRelationship(
                    "ui-event",
                    symbolName,
                    "invokes_handler",
                    "method",
                    handlerName,
                    $"line={lineNumber}"));
            }
        }

        return Task.FromResult(new CodeAnalysisResult(LanguageId, "parsed", symbols, relationships));
    }
}
