namespace CodeFlowIQ.Core.Models;

public sealed class Workspace
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string RootPath { get; set; }
    public WorkspaceKind Kind { get; set; }
    public string? GitRootPath { get; set; }
    public string? CurrentBranch { get; set; }
    public string? HeadCommitSha { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? LastIndexedAt { get; set; }
}
