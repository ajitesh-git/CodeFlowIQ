namespace CodeFlowIQ.Core.Analysis;

public interface ILanguageAnalyzer
{
    string LanguageId { get; }
    bool CanAnalyze(string filePath);
    Task<CodeAnalysisResult> AnalyzeAsync(string filePath, string content, CancellationToken cancellationToken);
}
