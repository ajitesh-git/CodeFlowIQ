namespace CodeFlowIQ.Core.Analysis;

public sealed record CodeAnalysisResult(
    string LanguageId,
    string Status,
    IReadOnlyList<DiscoveredSymbol> Symbols,
    IReadOnlyList<DiscoveredRelationship> Relationships,
    string? Error = null);
