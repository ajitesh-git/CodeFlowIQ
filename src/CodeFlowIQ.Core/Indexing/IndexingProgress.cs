namespace CodeFlowIQ.Core.Indexing;

public sealed record IndexingProgress(
    string Stage,
    int FilesScanned,
    int FilesIndexed,
    int FilesSkipped,
    int SymbolsIndexed,
    string? CurrentFile,
    string Message);
