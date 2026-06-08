namespace CodeFlowIQ.Core.Query;

public sealed record RepositoryOverview(
    string Name,
    string Kind,
    string Summary,
    IReadOnlyList<RepositoryOverviewItem> TechnologySignals,
    IReadOnlyList<RepositoryOverviewItem> SuggestedStartingPoints,
    IReadOnlyList<RepositoryOverviewItem> DetectedFlows,
    IReadOnlyList<RepositoryOverviewItem> ImportantApis,
    IReadOnlyList<RepositoryOverviewItem> DataTouchpoints,
    IReadOnlyList<RepositoryOverviewItem> AzureDependencies,
    IReadOnlyList<RepositoryOverviewItem> ImportantFolders);

public sealed record RepositoryOverviewItem(
    string Title,
    string Detail,
    string Kind,
    int Score);
