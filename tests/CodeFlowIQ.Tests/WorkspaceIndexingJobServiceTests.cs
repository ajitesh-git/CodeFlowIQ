using CodeFlowIQ.Core.Indexing;
using CodeFlowIQ.Indexing;

namespace CodeFlowIQ.Tests;

public sealed class WorkspaceIndexingJobServiceTests
{
    [Fact]
    public async Task Cancel_MarksActiveJobAsCancelled()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "codeflowiq-job-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);
        var service = new WorkspaceIndexingJobService(new WaitingIndexingService());

        var started = service.StartSync(workspacePath);
        Assert.True(started.State is "queued" or "running");

        var cancelled = service.Cancel(workspacePath);

        Assert.NotNull(cancelled);
        Assert.True(cancelled!.State is "cancelling" or "cancelled");

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var status = service.GetStatus(workspacePath);
            if (status?.State == "cancelled")
            {
                Assert.Equal("Cancelled", status.Stage);
                Assert.Contains("cancelled", status.Message, StringComparison.OrdinalIgnoreCase);
                return;
            }

            await Task.Delay(50);
        }

        Assert.Equal("cancelled", service.GetStatus(workspacePath)?.State);
    }

    private sealed class WaitingIndexingService : IWorkspaceIndexingService
    {
        public Task<IndexingSummary> InitializeAsync(
            string workspacePath,
            CancellationToken cancellationToken,
            IProgress<IndexingProgress>? progress = null) =>
            WaitAsync(workspacePath, cancellationToken, progress);

        public Task<IndexingSummary> SyncAsync(
            string workspacePath,
            CancellationToken cancellationToken,
            IProgress<IndexingProgress>? progress = null) =>
            WaitAsync(workspacePath, cancellationToken, progress);

        private static async Task<IndexingSummary> WaitAsync(
            string workspacePath,
            CancellationToken cancellationToken,
            IProgress<IndexingProgress>? progress)
        {
            progress?.Report(new IndexingProgress("Scanning", 1, 0, 0, 0, "Waiting.cs", "Scanning Waiting.cs."));
            await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            return new IndexingSummary(1, Path.GetFileName(workspacePath), 1, 1, 0, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        }
    }
}
