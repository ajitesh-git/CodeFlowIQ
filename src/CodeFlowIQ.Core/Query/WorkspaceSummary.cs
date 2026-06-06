namespace CodeFlowIQ.Core.Query;

public sealed record WorkspaceSummary(
    string Name,
    string Kind,
    int FileCount,
    int SymbolCount,
    int RelationshipCount,
    IReadOnlyList<string> LanguageCounts,
    IReadOnlyList<string> SymbolKindCounts,
    IReadOnlyList<string> RelationshipKindCounts,
    IReadOnlyList<string> AzureServiceCounts);
