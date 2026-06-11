namespace CodeFlowIQ.Core.Query;

public sealed record CSharpBackendTrace(
    string Query,
    string Status,
    IReadOnlyList<CSharpBackendTraceStep> Steps,
    IReadOnlyList<string> Warnings,
    int HiddenStepCount = 0,
    bool HasMore = false,
    string? ContinuationEntry = null,
    string? StopReason = null);

public sealed record CSharpBackendTraceStep(
    int Number,
    string Stage,
    string Title,
    string Detail,
    string Confidence,
    string Reason,
    string? EvidenceItemId,
    string? SourceKind,
    string? SourceIdentifier,
    string? TargetKind,
    string? TargetIdentifier,
    string? Metadata,
    string Category = "app",
    bool IsFrameworkCall = false,
    bool IsBoundary = false,
    bool IsHiddenByDefault = false,
    string? HiddenReason = null,
    string? SourceFilePath = null,
    int? SourceLineNumber = null,
    string? SourcePreview = null,
    string? ContinuationEntry = null);
