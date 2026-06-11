namespace CodeFlowIQ.Core.Query;

public sealed record RepositoryExplorerRelatedGroup(
    string Label,
    IReadOnlyList<RepositoryExplorerItem> Rows);
