namespace CodeFlowIQ.Core.Query;

public interface IWorkspaceQueryService
{
    Task<WorkspaceStatus?> GetStatusAsync(string workspacePath, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListFilesAsync(string workspacePath, string? languageId, int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> SearchSymbolsAsync(string workspacePath, string searchText, int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> SearchRelationshipsAsync(
        string workspacePath,
        string? searchText,
        string? relationshipKind,
        string? sourceText,
        string? targetText,
        bool includeTests,
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListApisAsync(
        string workspacePath,
        string? httpMethod,
        string? routeText,
        string? controllerText,
        bool includeTests,
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListAzureServicesAsync(
        string workspacePath,
        string? serviceText,
        bool includeTests,
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListFlowsAsync(
        string workspacePath,
        string? apiText,
        string? sourceText,
        string? handlerText,
        bool includeTests,
        int take,
        CancellationToken cancellationToken);

    Task<WorkspaceSummary?> GetSummaryAsync(string workspacePath, bool includeTests, int take, CancellationToken cancellationToken);
}
