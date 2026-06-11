namespace CodeFlowIQ.Core.Indexing;

public interface IWorkspaceIndexingJobService
{
    IndexingJobStatus StartInitialize(string workspacePath);
    IndexingJobStatus StartSync(string workspacePath);
    IndexingJobStatus? GetStatus(string workspacePath);
    IndexingJobStatus? Cancel(string workspacePath);
}
