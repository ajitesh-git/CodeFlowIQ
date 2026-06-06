namespace CodeFlowIQ.Core.Indexing;

public sealed record IndexingSummary(
    int WorkspaceId,
    string WorkspaceName,
    int FilesScanned,
    int FilesIndexed,
    int FilesSkipped,
    int SymbolsIndexed,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);
