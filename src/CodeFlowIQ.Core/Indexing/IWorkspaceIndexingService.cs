namespace CodeFlowIQ.Core.Indexing;

public interface IWorkspaceIndexingService
{
    Task<IndexingSummary> InitializeAsync(string workspacePath, CancellationToken cancellationToken);
    Task<IndexingSummary> SyncAsync(string workspacePath, CancellationToken cancellationToken);
}
