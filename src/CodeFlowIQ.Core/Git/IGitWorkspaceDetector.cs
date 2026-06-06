namespace CodeFlowIQ.Core.Git;

public interface IGitWorkspaceDetector
{
    GitWorkspaceInfo Detect(string workspacePath);
}
