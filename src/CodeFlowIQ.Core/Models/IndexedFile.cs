namespace CodeFlowIQ.Core.Models;

public sealed class IndexedFile
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }
    public required string RelativePath { get; set; }
    public required string FullPath { get; set; }
    public required string LanguageId { get; set; }
    public required string ContentHash { get; set; }
    public long SizeBytes { get; set; }
    public DateTimeOffset LastWriteTimeUtc { get; set; }
    public DateTimeOffset IndexedAt { get; set; }
    public bool IsDeleted { get; set; }
    public string? ParseStatus { get; set; }
    public string? ParseError { get; set; }
    public List<CodeSymbol> Symbols { get; set; } = [];
}
