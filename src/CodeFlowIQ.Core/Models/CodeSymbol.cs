namespace CodeFlowIQ.Core.Models;

public sealed class CodeSymbol
{
    public int Id { get; set; }
    public int IndexedFileId { get; set; }
    public IndexedFile? IndexedFile { get; set; }
    public required string Name { get; set; }
    public required string Kind { get; set; }
    public string? ContainerName { get; set; }
    public int LineNumber { get; set; }
    public int ColumnNumber { get; set; }
}
