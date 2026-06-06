namespace CodeFlowIQ.Core.Query;

public sealed record WorkspaceStatus(
    string Name,
    string RootPath,
    string Kind,
    string? CurrentBranch,
    string? HeadCommitSha,
    int FileCount,
    int SymbolCount,
    DateTimeOffset? LastIndexedAt);
