namespace CodeFlowIQ.Core.Git;

public sealed record GitWorkspaceInfo(
    bool IsGitRepository,
    string? GitRootPath,
    string? CurrentBranch,
    string? HeadCommitSha);
