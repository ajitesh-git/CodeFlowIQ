using System.Collections.Concurrent;
using CodeFlowIQ.Core.Indexing;

namespace CodeFlowIQ.Indexing;

public sealed class WorkspaceIndexingJobService(IWorkspaceIndexingService indexingService) : IWorkspaceIndexingJobService
{
    private readonly ConcurrentDictionary<string, IndexingJob> _jobs = new(StringComparer.OrdinalIgnoreCase);

    public IndexingJobStatus StartInitialize(string workspacePath) =>
        Start(workspacePath, "init", (path, token, progress) => indexingService.InitializeAsync(path, token, progress));

    public IndexingJobStatus StartSync(string workspacePath) =>
        Start(workspacePath, "sync", (path, token, progress) => indexingService.SyncAsync(path, token, progress));

    public IndexingJobStatus? GetStatus(string workspacePath)
    {
        var normalizedPath = NormalizeWorkspacePath(workspacePath);
        return _jobs.TryGetValue(normalizedPath, out var job) ? job.ToStatus() : null;
    }

    public IndexingJobStatus? Cancel(string workspacePath)
    {
        var normalizedPath = NormalizeWorkspacePath(workspacePath);
        if (!_jobs.TryGetValue(normalizedPath, out var job))
        {
            return null;
        }

        job.Cancel();
        return job.ToStatus();
    }

    private IndexingJobStatus Start(
        string workspacePath,
        string operation,
        Func<string, CancellationToken, IProgress<IndexingProgress>, Task<IndexingSummary>> runAsync)
    {
        var normalizedPath = NormalizeWorkspacePath(workspacePath);
        if (_jobs.TryGetValue(normalizedPath, out var existingJob) && existingJob.IsActive)
        {
            return existingJob.ToStatus();
        }

        var job = new IndexingJob(normalizedPath, operation);
        _jobs[normalizedPath] = job;

        _ = Task.Run(async () =>
        {
            job.MarkRunning();
            var progress = new Progress<IndexingProgress>(job.UpdateProgress);

            try
            {
                var summary = await runAsync(normalizedPath, job.CancellationToken, progress);
                job.MarkCompleted(summary);
            }
            catch (OperationCanceledException)
            {
                job.MarkCancelled();
            }
            catch (Exception ex)
            {
                job.MarkFailed(ex);
            }
            finally
            {
                job.Dispose();
            }
        });

        return job.ToStatus();
    }

    private static string NormalizeWorkspacePath(string workspacePath) =>
        Path.GetFullPath(workspacePath.Trim());

    private sealed class IndexingJob
    {
        private readonly object _lock = new();
        private readonly CancellationTokenSource _cancellation = new();
        private IndexingProgress _progress;
        private string _state = "queued";
        private DateTimeOffset _updatedAt;
        private DateTimeOffset? _completedAt;
        private IndexingSummary? _summary;
        private string? _error;

        public IndexingJob(string workspacePath, string operation)
        {
            JobId = Guid.NewGuid().ToString("N");
            WorkspacePath = workspacePath;
            Operation = operation;
            StartedAt = DateTimeOffset.UtcNow;
            _updatedAt = StartedAt;
            _progress = new IndexingProgress("Queued", 0, 0, 0, 0, null, "Indexing job is queued.");
        }

        public string JobId { get; }
        public string WorkspacePath { get; }
        public string Operation { get; }
        public DateTimeOffset StartedAt { get; }
        public CancellationToken CancellationToken => _cancellation.Token;

        public bool IsActive
        {
            get
            {
                lock (_lock)
                {
                    return _state is "queued" or "running" or "cancelling";
                }
            }
        }

        public void MarkRunning()
        {
            lock (_lock)
            {
                if (_state == "cancelling")
                {
                    return;
                }

                _state = "running";
                _updatedAt = DateTimeOffset.UtcNow;
                _progress = _progress with { Stage = "Preparing", Message = "Preparing workspace index." };
            }
        }

        public void UpdateProgress(IndexingProgress progress)
        {
            lock (_lock)
            {
                if (_state is "cancelling" or "completed" or "failed" or "cancelled")
                {
                    return;
                }

                _progress = progress;
                _updatedAt = DateTimeOffset.UtcNow;
            }
        }

        public void Cancel()
        {
            lock (_lock)
            {
                if (_state is not ("queued" or "running"))
                {
                    return;
                }

                _state = "cancelling";
                _updatedAt = DateTimeOffset.UtcNow;
                _progress = _progress with
                {
                    Stage = "Cancelling",
                    Message = "Stopping the indexing job after the current file."
                };
            }

            _cancellation.Cancel();
        }

        public void MarkCompleted(IndexingSummary summary)
        {
            lock (_lock)
            {
                _state = "completed";
                _summary = summary;
                _completedAt = DateTimeOffset.UtcNow;
                _updatedAt = _completedAt.Value;
                _progress = new IndexingProgress(
                    "Completed",
                    summary.FilesScanned,
                    summary.FilesIndexed,
                    summary.FilesSkipped,
                    summary.SymbolsIndexed,
                    null,
                    "Workspace index is ready.");
            }
        }

        public void MarkFailed(Exception exception)
        {
            lock (_lock)
            {
                _state = "failed";
                _error = exception.Message;
                _completedAt = DateTimeOffset.UtcNow;
                _updatedAt = _completedAt.Value;
                _progress = _progress with { Stage = "Failed", Message = exception.Message };
            }
        }

        public void MarkCancelled()
        {
            lock (_lock)
            {
                _state = "cancelled";
                _completedAt = DateTimeOffset.UtcNow;
                _updatedAt = _completedAt.Value;
                _progress = _progress with
                {
                    Stage = "Cancelled",
                    Message = "Indexing was cancelled."
                };
            }
        }

        public void Dispose() => _cancellation.Dispose();

        public IndexingJobStatus ToStatus()
        {
            lock (_lock)
            {
                return new IndexingJobStatus(
                    JobId,
                    WorkspacePath,
                    Operation,
                    _state,
                    _progress.Stage,
                    _progress.FilesScanned,
                    _progress.FilesIndexed,
                    _progress.FilesSkipped,
                    _progress.SymbolsIndexed,
                    _progress.CurrentFile,
                    _progress.Message,
                    StartedAt,
                    _updatedAt,
                    _completedAt,
                    _summary,
                    _error);
            }
        }
    }
}
