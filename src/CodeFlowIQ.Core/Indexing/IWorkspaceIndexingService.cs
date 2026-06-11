namespace CodeFlowIQ.Core.Indexing;

public interface IWorkspaceIndexingService
{
    Task<IndexingSummary> InitializeAsync(
        string workspacePath,
        CancellationToken cancellationToken,
        IProgress<IndexingProgress>? progress = null);

    Task<IndexingSummary> SyncAsync(
        string workspacePath,
        CancellationToken cancellationToken,
        IProgress<IndexingProgress>? progress = null);
}
