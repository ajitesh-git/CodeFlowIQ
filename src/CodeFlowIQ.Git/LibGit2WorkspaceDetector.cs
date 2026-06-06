using CodeFlowIQ.Core.Git;
using LibGit2Sharp;

namespace CodeFlowIQ.Git;

public sealed class LibGit2WorkspaceDetector : IGitWorkspaceDetector
{
    public GitWorkspaceInfo Detect(string workspacePath)
    {
        var discoveredPath = Repository.Discover(workspacePath);
        if (string.IsNullOrWhiteSpace(discoveredPath))
        {
            return new GitWorkspaceInfo(false, null, null, null);
        }

        using var repository = new Repository(discoveredPath);
        var workdir = repository.Info.WorkingDirectory?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return new GitWorkspaceInfo(
            true,
            workdir,
            repository.Head.FriendlyName,
            repository.Head.Tip?.Sha);
    }
}
