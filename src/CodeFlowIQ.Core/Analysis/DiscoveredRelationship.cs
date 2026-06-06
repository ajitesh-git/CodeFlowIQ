namespace CodeFlowIQ.Core.Analysis;

public sealed record DiscoveredRelationship(
    string SourceKind,
    string SourceIdentifier,
    string RelationshipKind,
    string TargetKind,
    string TargetIdentifier,
    string? Metadata);
