using CodeFlowIQ.Analyzers;
using CodeFlowIQ.Core.Analysis;
using CodeFlowIQ.Core.Git;
using CodeFlowIQ.Core.Indexing;
using CodeFlowIQ.Core.Query;
using CodeFlowIQ.Git;
using CodeFlowIQ.Indexing;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddSingleton<IndexingOptions>();
services.AddSingleton<IGitWorkspaceDetector, LibGit2WorkspaceDetector>();
services.AddSingleton<ILanguageDetector, LanguageDetector>();
services.AddSingleton<ILanguageAnalyzer, CSharpLanguageAnalyzer>();
services.AddSingleton<ILanguageAnalyzer, SqlLanguageAnalyzer>();
services.AddSingleton<ILanguageAnalyzer, JavaScriptTypeScriptLanguageAnalyzer>();
services.AddSingleton<ILanguageAnalyzer, AngularTemplateLanguageAnalyzer>();
services.AddSingleton<IWorkspaceIndexingService, WorkspaceIndexingService>();
services.AddSingleton<IWorkspaceQueryService, WorkspaceQueryService>();

await using var provider = services.BuildServiceProvider();
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    return await RunAsync(args, provider, cancellation.Token);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Operation cancelled.");
    return 130;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static async Task<int> RunAsync(string[] args, IServiceProvider provider, CancellationToken cancellationToken)
{
    if (args.Length == 0 || args[0] is "-h" or "--help")
    {
        WriteHelp();
        return 0;
    }

    var command = args[0].ToLowerInvariant();
    var workspacePath = GetWorkspacePath(args);

    switch (command)
    {
        case "init":
            RequirePath(args, "init");
            await RunIndexAsync(provider.GetRequiredService<IWorkspaceIndexingService>(), workspacePath, true, cancellationToken);
            return 0;

        case "sync":
            await RunIndexAsync(provider.GetRequiredService<IWorkspaceIndexingService>(), workspacePath, false, cancellationToken);
            return 0;

        case "status":
            await RunStatusAsync(provider.GetRequiredService<IWorkspaceQueryService>(), workspacePath, cancellationToken);
            return 0;

        case "files":
            await RunFilesAsync(provider.GetRequiredService<IWorkspaceQueryService>(), workspacePath, args, cancellationToken);
            return 0;

        case "symbols":
            await RunSymbolsAsync(provider.GetRequiredService<IWorkspaceQueryService>(), workspacePath, args, cancellationToken);
            return 0;

        case "relationships":
            await RunRelationshipsAsync(provider.GetRequiredService<IWorkspaceQueryService>(), workspacePath, args, cancellationToken);
            return 0;

        case "apis":
            await RunApisAsync(provider.GetRequiredService<IWorkspaceQueryService>(), workspacePath, args, cancellationToken);
            return 0;

        case "azure":
            await RunAzureAsync(provider.GetRequiredService<IWorkspaceQueryService>(), workspacePath, args, cancellationToken);
            return 0;

        case "summary":
            await RunSummaryAsync(provider.GetRequiredService<IWorkspaceQueryService>(), workspacePath, args, cancellationToken);
            return 0;

        case "flows":
            await RunFlowsAsync(provider.GetRequiredService<IWorkspaceQueryService>(), workspacePath, args, cancellationToken);
            return 0;

        case "chains":
            await RunChainsAsync(provider.GetRequiredService<IWorkspaceQueryService>(), workspacePath, args, cancellationToken);
            return 0;

        case "backend":
            await RunBackendAsync(provider.GetRequiredService<IWorkspaceQueryService>(), workspacePath, args, cancellationToken);
            return 0;

        default:
            Console.Error.WriteLine($"Unknown command: {args[0]}");
            WriteHelp();
            return 2;
    }
}

static async Task RunIndexAsync(IWorkspaceIndexingService indexingService, string workspacePath, bool initialize, CancellationToken cancellationToken)
{
    var summary = initialize
        ? await indexingService.InitializeAsync(workspacePath, cancellationToken)
        : await indexingService.SyncAsync(workspacePath, cancellationToken);

    Console.WriteLine($"{(initialize ? "Initialized" : "Synced")} workspace: {summary.WorkspaceName}");
    Console.WriteLine($"Files scanned:  {summary.FilesScanned}");
    Console.WriteLine($"Files indexed:  {summary.FilesIndexed}");
    Console.WriteLine($"Files skipped:  {summary.FilesSkipped}");
    Console.WriteLine($"Symbols indexed:{summary.SymbolsIndexed}");
    Console.WriteLine($"Duration:       {(summary.CompletedAt - summary.StartedAt).TotalSeconds:N1}s");
}

static async Task RunStatusAsync(IWorkspaceQueryService queryService, string workspacePath, CancellationToken cancellationToken)
{
    var status = await queryService.GetStatusAsync(workspacePath, cancellationToken);
    if (status is null)
    {
        Console.WriteLine("Workspace has not been initialized yet.");
        return;
    }

    Console.WriteLine($"Workspace:      {status.Name}");
    Console.WriteLine($"Root:           {status.RootPath}");
    Console.WriteLine($"Kind:           {status.Kind}");
    Console.WriteLine($"Branch:         {status.CurrentBranch ?? "n/a"}");
    Console.WriteLine($"HEAD:           {status.HeadCommitSha ?? "n/a"}");
    Console.WriteLine($"Files:          {status.FileCount}");
    Console.WriteLine($"Symbols:        {status.SymbolCount}");
    Console.WriteLine($"Last indexed:   {status.LastIndexedAt?.ToString("u") ?? "never"}");
}

static async Task RunFilesAsync(IWorkspaceQueryService queryService, string workspacePath, string[] args, CancellationToken cancellationToken)
{
    var language = GetOption(args, "--language");
    var folder = GetOption(args, "--folder");
    var take = GetIntOption(args, "--take", 200);
    var rows = await queryService.ListFilesAsync(workspacePath, language, folder, take, cancellationToken);
    foreach (var row in rows)
    {
        Console.WriteLine(row);
    }
}

static async Task RunSymbolsAsync(IWorkspaceQueryService queryService, string workspacePath, string[] args, CancellationToken cancellationToken)
{
    var searchText = GetFirstPositionalArgument(args);
    if (string.IsNullOrWhiteSpace(searchText))
    {
        throw new ArgumentException("symbols requires a search term.");
    }

    var take = GetIntOption(args, "--take", 200);
    var rows = await queryService.SearchSymbolsAsync(workspacePath, searchText, take, cancellationToken);
    foreach (var row in rows)
    {
        Console.WriteLine(row);
    }
}

static async Task RunRelationshipsAsync(IWorkspaceQueryService queryService, string workspacePath, string[] args, CancellationToken cancellationToken)
{
    var searchText = GetFirstPositionalArgument(args);
    var take = GetIntOption(args, "--take", 50);
    var kind = GetOption(args, "--kind");
    var source = GetOption(args, "--source");
    var target = GetOption(args, "--target");
    var includeTests = GetBoolOption(args, "--include-tests", false);
    var rows = await queryService.SearchRelationshipsAsync(workspacePath, searchText, kind, source, target, includeTests, take, cancellationToken);
    foreach (var row in rows)
    {
        Console.WriteLine(row);
    }
}

static async Task RunApisAsync(IWorkspaceQueryService queryService, string workspacePath, string[] args, CancellationToken cancellationToken)
{
    var rows = await queryService.ListApisAsync(
        workspacePath,
        GetOption(args, "--method"),
        GetOption(args, "--route"),
        GetOption(args, "--controller"),
        GetBoolOption(args, "--include-tests", false),
        GetIntOption(args, "--take", 50),
        cancellationToken);

    foreach (var row in rows)
    {
        Console.WriteLine(row);
    }
}

static async Task RunAzureAsync(IWorkspaceQueryService queryService, string workspacePath, string[] args, CancellationToken cancellationToken)
{
    var rows = await queryService.ListAzureServicesAsync(
        workspacePath,
        GetOption(args, "--service"),
        GetBoolOption(args, "--include-tests", false),
        GetIntOption(args, "--take", 50),
        cancellationToken);

    foreach (var row in rows)
    {
        Console.WriteLine(row);
    }
}

static async Task RunSummaryAsync(IWorkspaceQueryService queryService, string workspacePath, string[] args, CancellationToken cancellationToken)
{
    var summary = await queryService.GetSummaryAsync(
        workspacePath,
        GetBoolOption(args, "--include-tests", false),
        GetIntOption(args, "--take", 10),
        cancellationToken);

    if (summary is null)
    {
        Console.WriteLine("Workspace has not been initialized yet.");
        return;
    }

    Console.WriteLine($"Workspace:     {summary.Name}");
    Console.WriteLine($"Kind:          {summary.Kind}");
    Console.WriteLine($"Files:         {summary.FileCount}");
    Console.WriteLine($"Symbols:       {summary.SymbolCount}");
    Console.WriteLine($"Relationships: {summary.RelationshipCount}");
    WriteSection("Languages", summary.LanguageCounts);
    WriteSection("Symbol Kinds", summary.SymbolKindCounts);
    WriteSection("Relationship Kinds", summary.RelationshipKindCounts);
    WriteSection("Azure Services", summary.AzureServiceCounts);
}

static async Task RunFlowsAsync(IWorkspaceQueryService queryService, string workspacePath, string[] args, CancellationToken cancellationToken)
{
    var rows = await queryService.ListFlowsAsync(
        workspacePath,
        GetOption(args, "--api"),
        GetOption(args, "--source"),
        GetOption(args, "--handler"),
        GetBoolOption(args, "--include-tests", false),
        GetIntOption(args, "--take", 50),
        cancellationToken);

    foreach (var row in rows)
    {
        Console.WriteLine(row);
    }
}

static async Task RunChainsAsync(IWorkspaceQueryService queryService, string workspacePath, string[] args, CancellationToken cancellationToken)
{
    var rows = await queryService.ListFlowChainsAsync(
        workspacePath,
        GetOption(args, "--api"),
        GetOption(args, "--source"),
        GetOption(args, "--target"),
        GetOption(args, "--format"),
        GetBoolOption(args, "--include-tests", false),
        GetIntOption(args, "--depth", 8),
        GetIntOption(args, "--take", 20),
        cancellationToken);

    foreach (var row in rows)
    {
        Console.WriteLine(row);
    }
}

static async Task RunBackendAsync(IWorkspaceQueryService queryService, string workspacePath, string[] args, CancellationToken cancellationToken)
{
    var kind = GetOption(args, "--kind");
    var take = GetIntOption(args, "--take", 50);
    string[] relationshipKinds = string.IsNullOrWhiteSpace(kind)
        ? ["depends_on", "implemented_by", "calls_method", "executes_procedure", "reads_table", "writes_table", "saves_changes"]
        : [kind];

    var rows = new List<string>();
    foreach (var relationshipKind in relationshipKinds)
    {
        rows.AddRange(await queryService.SearchRelationshipsAsync(
            workspacePath,
            GetFirstPositionalArgument(args),
            relationshipKind,
            GetOption(args, "--source"),
            GetOption(args, "--target"),
            GetBoolOption(args, "--include-tests", false),
            take,
            cancellationToken));
    }

    foreach (var row in rows.Distinct(StringComparer.Ordinal).Take(take))
    {
        Console.WriteLine(row);
    }
}

static string GetWorkspacePath(string[] args)
{
    var pathOption = GetOption(args, "--path");
    if (!string.IsNullOrWhiteSpace(pathOption))
    {
        return Path.GetFullPath(pathOption);
    }

    if (args.Length >= 2 && !args[1].StartsWith("--", StringComparison.Ordinal) && args[0] is "init")
    {
        return Path.GetFullPath(args[1]);
    }

    return Directory.GetCurrentDirectory();
}

static string? GetOption(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return null;
}

static string? GetFirstPositionalArgument(string[] args)
{
    for (var i = 1; i < args.Length; i++)
    {
        if (args[i].StartsWith("--", StringComparison.Ordinal))
        {
            i++;
            continue;
        }

        return args[i];
    }

    return null;
}

static int GetIntOption(string[] args, string name, int defaultValue)
{
    var value = GetOption(args, name);
    return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : defaultValue;
}

static bool GetBoolOption(string[] args, string name, bool defaultValue)
{
    var value = GetOption(args, name);
    if (value is null)
    {
        return defaultValue;
    }

    return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
}

static void RequirePath(string[] args, string command)
{
    if (args.Length < 2 && GetOption(args, "--path") is null)
    {
        throw new ArgumentException($"{command} requires a workspace path.");
    }
}

static void WriteHelp()
{
    Console.WriteLine("CodeFlowIQ");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  codeflowiq init <workspace-path>");
    Console.WriteLine("  codeflowiq sync [--path <workspace-path>]");
    Console.WriteLine("  codeflowiq status [--path <workspace-path>]");
    Console.WriteLine("  codeflowiq files [--language <language-id>] [--folder <folder-text>] [--path <workspace-path>]");
    Console.WriteLine("  codeflowiq symbols <search-text> [--take <n>] [--path <workspace-path>]");
    Console.WriteLine("  codeflowiq relationships [search-text] [--kind <kind>] [--source <text>] [--target <text>] [--take <n>] [--include-tests true|false] [--path <workspace-path>]");
    Console.WriteLine("  codeflowiq apis [--method <GET|POST|...>] [--route <text>] [--controller <text>] [--take <n>] [--include-tests true|false] [--path <workspace-path>]");
    Console.WriteLine("  codeflowiq azure [--service <text>] [--take <n>] [--include-tests true|false] [--path <workspace-path>]");
    Console.WriteLine("  codeflowiq summary [--take <n>] [--include-tests true|false] [--path <workspace-path>]");
    Console.WriteLine("  codeflowiq flows [--api <text>] [--source <text>] [--handler <text>] [--take <n>] [--include-tests true|false] [--path <workspace-path>]");
    Console.WriteLine("  codeflowiq chains [--api <text>] [--source <text>] [--target <text>] [--format compact|tree|json] [--depth <n>] [--take <n>] [--include-tests true|false] [--path <workspace-path>]");
    Console.WriteLine("  codeflowiq backend [search-text] [--kind <kind>] [--source <text>] [--target <text>] [--take <n>] [--include-tests true|false] [--path <workspace-path>]");
}

static void WriteSection(string title, IReadOnlyList<string> rows)
{
    Console.WriteLine();
    Console.WriteLine(title + ":");
    foreach (var row in rows)
    {
        Console.WriteLine("  " + row);
    }
}
