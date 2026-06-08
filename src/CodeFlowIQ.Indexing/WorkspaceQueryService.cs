using CodeFlowIQ.Core.Query;

namespace CodeFlowIQ.Indexing;

public sealed class WorkspaceQueryService : IWorkspaceQueryService
{
    private readonly WorkspaceInventoryQueryHandler _inventory;
    private readonly FlowChainQueryHandler _flowChains;
    private readonly RepositoryOverviewQueryHandler _overview;
    private readonly RuntimeFlowQueryHandler _runtimeFlows;

    public WorkspaceQueryService()
        : this(
            new WorkspaceInventoryQueryHandler(),
            new FlowChainQueryHandler(),
            new RepositoryOverviewQueryHandler(),
            new RuntimeFlowQueryHandler())
    {
    }

    public WorkspaceQueryService(
        WorkspaceInventoryQueryHandler inventory,
        FlowChainQueryHandler flowChains,
        RepositoryOverviewQueryHandler overview,
        RuntimeFlowQueryHandler runtimeFlows)
    {
        _inventory = inventory;
        _flowChains = flowChains;
        _overview = overview;
        _runtimeFlows = runtimeFlows;
    }

    public Task<WorkspaceStatus?> GetStatusAsync(string workspacePath, CancellationToken cancellationToken) =>
        _inventory.GetStatusAsync(workspacePath, cancellationToken);

    public Task<IReadOnlyList<string>> ListFilesAsync(
        string workspacePath,
        string? languageId,
        string? folderText,
        int take,
        CancellationToken cancellationToken) =>
        _inventory.ListFilesAsync(workspacePath, languageId, folderText, take, cancellationToken);

    public Task<IReadOnlyList<string>> SearchSymbolsAsync(
        string workspacePath,
        string searchText,
        int take,
        CancellationToken cancellationToken) =>
        _inventory.SearchSymbolsAsync(workspacePath, searchText, take, cancellationToken);

    public Task<IReadOnlyList<string>> SearchRelationshipsAsync(
        string workspacePath,
        string? searchText,
        string? relationshipKind,
        string? sourceText,
        string? targetText,
        bool includeTests,
        int take,
        CancellationToken cancellationToken) =>
        _inventory.SearchRelationshipsAsync(
            workspacePath,
            searchText,
            relationshipKind,
            sourceText,
            targetText,
            includeTests,
            take,
            cancellationToken);

    public Task<IReadOnlyList<string>> ListApisAsync(
        string workspacePath,
        string? httpMethod,
        string? routeText,
        string? controllerText,
        bool includeTests,
        int take,
        CancellationToken cancellationToken) =>
        _inventory.ListApisAsync(workspacePath, httpMethod, routeText, controllerText, includeTests, take, cancellationToken);

    public Task<IReadOnlyList<string>> ListAzureServicesAsync(
        string workspacePath,
        string? serviceText,
        bool includeTests,
        int take,
        CancellationToken cancellationToken) =>
        _inventory.ListAzureServicesAsync(workspacePath, serviceText, includeTests, take, cancellationToken);

    public Task<IReadOnlyList<RepositoryExplorerItem>> ListRepositoryExplorerItemsAsync(
        string workspacePath,
        string surface,
        string? queryText,
        string? selectedItemId,
        bool includeTests,
        int take,
        CancellationToken cancellationToken) =>
        _inventory.ListRepositoryExplorerItemsAsync(workspacePath, surface, queryText, selectedItemId, includeTests, take, cancellationToken);

    public Task<IReadOnlyList<string>> ListFlowsAsync(
        string workspacePath,
        string? apiText,
        string? sourceText,
        string? handlerText,
        bool includeTests,
        int take,
        CancellationToken cancellationToken) =>
        _flowChains.ListFlowsAsync(workspacePath, apiText, sourceText, handlerText, includeTests, take, cancellationToken);

    public Task<IReadOnlyList<string>> ListFlowChainsAsync(
        string workspacePath,
        string? apiText,
        string? sourceText,
        string? targetText,
        string? format,
        bool includeTests,
        int maxDepth,
        int take,
        CancellationToken cancellationToken) =>
        _flowChains.ListFlowChainsAsync(
            workspacePath,
            apiText,
            sourceText,
            targetText,
            format,
            includeTests,
            maxDepth,
            take,
            cancellationToken);

    public Task<WorkspaceSummary?> GetSummaryAsync(
        string workspacePath,
        bool includeTests,
        int take,
        CancellationToken cancellationToken) =>
        _inventory.GetSummaryAsync(workspacePath, includeTests, take, cancellationToken);

    public Task<RepositoryOverview?> GetRepositoryOverviewAsync(
        string workspacePath,
        bool includeTests,
        int take,
        CancellationToken cancellationToken) =>
        _overview.GetRepositoryOverviewAsync(workspacePath, includeTests, take, cancellationToken);

    public Task<RuntimeFlowMap?> GetRuntimeFlowMapAsync(
        string workspacePath,
        bool includeTests,
        int take,
        CancellationToken cancellationToken) =>
        _runtimeFlows.GetRuntimeFlowMapAsync(workspacePath, includeTests, take, cancellationToken);
}
