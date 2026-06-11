namespace CodeFlowIQ.Core.Query;

public sealed record RepositoryExplorerItem(
    string Id,
    string Surface,
    string Title,
    string Subtitle,
    string Detail,
    string SourceKind,
    string SourceIdentifier,
    string? RelationshipKind,
    string? TargetKind,
    string? TargetIdentifier,
    string? FilePath,
    int? LineNumber,
    string? Metadata,
    string? DisplayTitle = null,
    string? DisplaySubtitle = null,
    string? DisplayLocator = null,
    string? EvidenceSummary = null,
    string? OccurrenceKey = null);
