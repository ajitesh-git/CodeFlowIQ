namespace CodeFlowIQ.Core.Query;

public sealed record RuntimeFlowMap(
    string Name,
    string Kind,
    string Summary,
    IReadOnlyList<RuntimeEntryPoint> EntryPoints,
    IReadOnlyList<RuntimeExecutionPath> ExecutionPaths,
    IReadOnlyList<RuntimeFlow> Flows);

public sealed record RuntimeEntryPoint(
    string Title,
    string Detail,
    string Category,
    int Confidence,
    string? RepositoryExplorerItemId = null);

public sealed record RuntimeFlow(
    string Title,
    string Summary,
    string Category,
    int Confidence,
    IReadOnlyList<RuntimeFlowStep> Steps);

public sealed record RuntimeExecutionPath(
    string EntryPointTitle,
    string EntryPointDetail,
    string Category,
    string Summary,
    IReadOnlyList<RuntimeFlow> Flows);

public sealed record RuntimeFlowStep(
    string Stage,
    string Title,
    string Detail,
    string Kind,
    string? EvidenceType = null,
    string? ExplorerSurface = null,
    string? ExplorerQuery = null,
    string? RepositoryExplorerItemId = null);
