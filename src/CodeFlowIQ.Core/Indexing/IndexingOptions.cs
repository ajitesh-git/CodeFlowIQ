namespace CodeFlowIQ.Core.Indexing;

public sealed class IndexingOptions
{
    public string IgnoreFileName { get; set; } = ".codeflowiqignore";
    public int MaxFileSizeKb { get; set; } = 1024;
    public int MaxParallelism { get; set; } = 2;
    public bool SkipGeneratedFiles { get; set; } = true;
    public string[] ExcludedDirectories { get; set; } =
    [
        ".git",
        ".codeflowiq",
        ".vs",
        ".idea",
        "bin",
        "obj",
        "node_modules",
        "dist",
        "build",
        ".next",
        "coverage",
        "packages"
    ];
}
