namespace CodeFlowIQ.Core.Models;

public sealed class CodeRelationship
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }
    public required string SourceKind { get; set; }
    public required string SourceIdentifier { get; set; }
    public required string RelationshipKind { get; set; }
    public required string TargetKind { get; set; }
    public required string TargetIdentifier { get; set; }
    public string? Metadata { get; set; }
    public DateTimeOffset DiscoveredAt { get; set; }
}
