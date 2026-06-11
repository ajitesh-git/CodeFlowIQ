namespace CodeFlowIQ.Core.Indexing;

public sealed record IndexingJobStatus(
    string JobId,
    string WorkspacePath,
    string Operation,
    string State,
    string Stage,
    int FilesScanned,
    int FilesIndexed,
    int FilesSkipped,
    int SymbolsIndexed,
    string? CurrentFile,
    string Message,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    IndexingSummary? Summary,
    string? Error);
