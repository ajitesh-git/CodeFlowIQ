namespace CodeFlowIQ.Core.Analysis;

public sealed record DiscoveredSymbol(
    string Name,
    string Kind,
    string? ContainerName,
    int LineNumber,
    int ColumnNumber);
