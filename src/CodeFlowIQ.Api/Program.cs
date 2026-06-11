using CodeFlowIQ.Api;
using CodeFlowIQ.Analyzers;
using CodeFlowIQ.Core.Analysis;
using CodeFlowIQ.Core.Git;
using CodeFlowIQ.Core.Indexing;
using CodeFlowIQ.Core.Query;
using CodeFlowIQ.Git;
using CodeFlowIQ.Indexing;

var builder = WebApplication.CreateBuilder(args);
var runtimeOptions = LocalRuntimeHost.CreateOptions(builder.Configuration);

builder.WebHost.UseUrls(runtimeOptions.ListenUrl);
builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalUi", policy =>
        policy
            .SetIsOriginAllowed(origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                && (uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                    || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)))
            .AllowAnyHeader()
            .AllowAnyMethod());
});
builder.Services.AddOpenApi();
builder.Services.AddSingleton(runtimeOptions);
builder.Services.AddSingleton<IndexingOptions>();
builder.Services.AddSingleton<IGitWorkspaceDetector, LibGit2WorkspaceDetector>();
builder.Services.AddSingleton<ILanguageDetector, LanguageDetector>();
builder.Services.AddSingleton<ILanguageAnalyzer, CSharpLanguageAnalyzer>();
builder.Services.AddSingleton<ILanguageAnalyzer, SqlLanguageAnalyzer>();
builder.Services.AddSingleton<ILanguageAnalyzer, JavaScriptTypeScriptLanguageAnalyzer>();
builder.Services.AddSingleton<ILanguageAnalyzer, AngularTemplateLanguageAnalyzer>();
builder.Services.AddSingleton<IWorkspaceIndexingService, WorkspaceIndexingService>();
builder.Services.AddSingleton<IWorkspaceIndexingJobService, WorkspaceIndexingJobService>();
builder.Services.AddSingleton<WorkspaceInventoryQueryHandler>();
builder.Services.AddSingleton<FlowChainQueryHandler>();
builder.Services.AddSingleton<RepositoryOverviewQueryHandler>();
builder.Services.AddSingleton<RuntimeFlowQueryHandler>();
builder.Services.AddSingleton<CSharpBackendTraceQueryHandler>();
builder.Services.AddSingleton<IWorkspaceQueryService, WorkspaceQueryService>();

var app = builder.Build();
app.UseCors("LocalUi");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => Results.Ok(new
{
    name = "CodeFlowIQ.Api",
    status = "running",
    runtimeFile = runtimeOptions.WriteRuntimeFile ? runtimeOptions.RuntimeFilePath : null,
    endpoints = new[]
    {
        "/health",
        "/openapi/v1.json",
        "/api/workspace/status",
        "/api/workspace/indexing-status",
        "/api/workspace/indexing-cancel",
        "/api/workspace/init",
        "/api/workspace/sync",
        "/api/overview",
        "/api/runtime-flows",
        "/api/summary",
        "/api/files",
        "/api/symbols",
        "/api/relationships",
        "/api/apis",
        "/api/azure",
        "/api/explorer",
        "/api/explorer/related",
        "/api/flows",
        "/api/chains",
        "/api/backend",
        "/api/csharp-backend-trace",
        "/api/csharp-backend-trace/entries"
    }
}));

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    name = "CodeFlowIQ.Api",
    processId = Environment.ProcessId,
    runtimeFile = runtimeOptions.WriteRuntimeFile ? runtimeOptions.RuntimeFilePath : null
}));

var api = app.MapGroup("/api");

api.MapGet("/workspace/status", async (
    string path,
    IWorkspaceQueryService queryService,
    CancellationToken cancellationToken) =>
{
    var status = await queryService.GetStatusAsync(path, cancellationToken);
    return status is null ? Results.NotFound(new { message = "Workspace has not been initialized yet." }) : Results.Ok(status);
});

api.MapGet("/workspace/indexing-status", (
    string path,
    IWorkspaceIndexingJobService indexingJobs) =>
{
    var status = indexingJobs.GetStatus(path);
    return status is null ? Results.NotFound(new { message = "No indexing job has been started for this workspace." }) : Results.Ok(status);
});

api.MapPost("/workspace/indexing-cancel", (
    WorkspacePathRequest request,
    IWorkspaceIndexingJobService indexingJobs) =>
{
    if (string.IsNullOrWhiteSpace(request.Path))
    {
        return Results.BadRequest(new { message = "Path is required." });
    }

    var status = indexingJobs.Cancel(request.Path);
    return status is null ? Results.NotFound(new { message = "No indexing job has been started for this workspace." }) : Results.Ok(status);
});

api.MapPost("/workspace/init", async (
    WorkspacePathRequest request,
    IWorkspaceIndexingJobService indexingJobs,
    IWorkspaceQueryService queryService,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Path))
    {
        return Results.BadRequest(new { message = "Path is required." });
    }

    var existingStatus = await queryService.GetStatusAsync(request.Path, cancellationToken);
    if (existingStatus?.LastIndexedAt is not null)
    {
        var completedAt = DateTimeOffset.UtcNow;
        return Results.Ok(new
        {
            workspaceId = 0,
            workspaceName = existingStatus.Name,
            filesScanned = 0,
            filesIndexed = 0,
            filesSkipped = 0,
            symbolsIndexed = existingStatus.SymbolCount,
            startedAt = completedAt,
            completedAt,
            reusedExistingIndex = true,
            message = "Using the existing workspace index. Run sync when you want to refresh changed files."
        });
    }

    var status = indexingJobs.StartInitialize(request.Path);
    return Results.Json(status, statusCode: StatusCodes.Status202Accepted);
});

api.MapPost("/workspace/sync", async (
    WorkspacePathRequest request,
    IWorkspaceIndexingJobService indexingJobs) =>
{
    if (string.IsNullOrWhiteSpace(request.Path))
    {
        return Results.BadRequest(new { message = "Path is required." });
    }

    var status = indexingJobs.StartSync(request.Path);
    return Results.Json(status, statusCode: StatusCodes.Status202Accepted);
});

api.MapGet("/summary", async (
    string path,
    bool? includeTests,
    int? take,
    IWorkspaceQueryService queryService,
    CancellationToken cancellationToken) =>
    Results.Ok(await queryService.GetSummaryAsync(path, includeTests == true, NormalizeTake(take, 10), cancellationToken)));

api.MapGet("/overview", async (
    string path,
    bool? includeTests,
    int? take,
    IWorkspaceQueryService queryService,
    CancellationToken cancellationToken) =>
    Results.Ok(await queryService.GetRepositoryOverviewAsync(path, includeTests == true, NormalizeTake(take, 10), cancellationToken)));

api.MapGet("/runtime-flows", async (
    string path,
    bool? includeTests,
    int? take,
    IWorkspaceQueryService queryService,
    CancellationToken cancellationToken) =>
    Results.Ok(await queryService.GetRuntimeFlowMapAsync(path, includeTests == true, NormalizeTake(take, 10), cancellationToken)));

api.MapGet("/files", async (
    string path,
    string? language,
    string? folder,
    int? take,
    IWorkspaceQueryService queryService,
    CancellationToken cancellationToken) =>
    Results.Ok(await queryService.ListFilesAsync(path, language, folder, NormalizeTake(take, 200), cancellationToken)));

api.MapGet("/symbols", async (
    string path,
    string q,
    int? take,
    IWorkspaceQueryService queryService,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(q))
    {
        return Results.BadRequest(new { message = "Query text is required." });
    }

    return Results.Ok(await queryService.SearchSymbolsAsync(path, q, NormalizeTake(take, 200), cancellationToken));
});

api.MapGet("/relationships", async (
    string path,
    string? q,
    string? kind,
    string? source,
    string? target,
    bool? includeTests,
    int? take,
    IWorkspaceQueryService queryService,
    CancellationToken cancellationToken) =>
    Results.Ok(await queryService.SearchRelationshipsAsync(path, q, kind, source, target, includeTests == true, NormalizeTake(take, 50), cancellationToken)));

api.MapGet("/apis", async (
    string path,
    string? method,
    string? route,
    string? controller,
    bool? includeTests,
    int? take,
    IWorkspaceQueryService queryService,
    CancellationToken cancellationToken) =>
    Results.Ok(await queryService.ListApisAsync(path, method, route, controller, includeTests == true, NormalizeTake(take, 50), cancellationToken)));

api.MapGet("/azure", async (
    string path,
    string? service,
    bool? includeTests,
    int? take,
    IWorkspaceQueryService queryService,
    CancellationToken cancellationToken) =>
    Results.Ok(await queryService.ListAzureServicesAsync(path, service, includeTests == true, NormalizeTake(take, 50), cancellationToken)));

api.MapGet("/explorer", async (
    string path,
    string surface,
    string? q,
    string? selectedItemId,
    bool? includeTests,
    int? take,
    IWorkspaceQueryService queryService,
    CancellationToken cancellationToken) =>
    Results.Ok(await queryService.ListRepositoryExplorerItemsAsync(path, surface, q, selectedItemId, includeTests == true, NormalizeTake(take, 200), cancellationToken)));

api.MapGet("/explorer/related", async (
    string path,
    string surface,
    string itemId,
    bool? includeTests,
    int? take,
    IWorkspaceQueryService queryService,
    CancellationToken cancellationToken) =>
    Results.Ok(await queryService.ListRepositoryExplorerRelatedItemsAsync(path, surface, itemId, includeTests == true, NormalizeTake(take, 6), cancellationToken)));

api.MapGet("/flows", async (
    string path,
    string? api,
    string? source,
    string? handler,
    bool? includeTests,
    int? take,
    IWorkspaceQueryService queryService,
    CancellationToken cancellationToken) =>
    Results.Ok(await queryService.ListFlowsAsync(path, api, source, handler, includeTests == true, NormalizeTake(take, 50), cancellationToken)));

api.MapGet("/chains", async (
    string path,
    string? api,
    string? source,
    string? target,
    string? format,
    bool? includeTests,
    int? depth,
    int? take,
    IWorkspaceQueryService queryService,
    CancellationToken cancellationToken) =>
    Results.Ok(await queryService.ListFlowChainsAsync(path, api, source, target, format, includeTests == true, NormalizeTake(depth, 8), NormalizeTake(take, 20), cancellationToken)));

api.MapGet("/csharp-backend-trace", async (
    string path,
    string entry,
    bool? includeTests,
    int? depth,
    IWorkspaceQueryService queryService,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(entry))
    {
        return Results.BadRequest(new { message = "Entry route or method text is required." });
    }

    var trace = await queryService.GetCSharpBackendTraceAsync(path, entry, includeTests == true, NormalizeTake(depth, 24), cancellationToken);
    return trace is null ? Results.NotFound(new { message = "Workspace has not been initialized yet." }) : Results.Ok(trace);
});

api.MapGet("/csharp-backend-trace/entries", async (
    string path,
    bool? includeTests,
    int? take,
    IWorkspaceQueryService queryService,
    CancellationToken cancellationToken) =>
    Results.Ok(await queryService.ListCSharpBackendTraceEntriesAsync(path, includeTests == true, NormalizeTake(take, 1000), cancellationToken)));

api.MapGet("/backend", async (
    string path,
    string? q,
    string? kind,
    string? source,
    string? target,
    bool? includeTests,
    int? take,
    IWorkspaceQueryService queryService,
    CancellationToken cancellationToken) =>
{
    string[] relationshipKinds = string.IsNullOrWhiteSpace(kind)
        ? ["depends_on", "implemented_by", "calls_method", "executes_procedure", "reads_table", "writes_table", "saves_changes"]
        : [kind];

    var rows = new List<string>();
    var rowLimit = NormalizeTake(take, 50);
    foreach (var relationshipKind in relationshipKinds)
    {
        rows.AddRange(await queryService.SearchRelationshipsAsync(
            path,
            q,
            relationshipKind,
            source,
            target,
            includeTests == true,
            rowLimit,
            cancellationToken));
    }

    return Results.Ok(rows.Distinct(StringComparer.Ordinal).Take(rowLimit).ToList());
});

app.Lifetime.ApplicationStarted.Register(() => LocalRuntimeHost.TryWriteRuntimeFile(app, runtimeOptions));

app.Run();

static int NormalizeTake(int? value, int defaultValue) =>
    value is > 0 and <= 10000 ? value.Value : defaultValue;

public sealed record WorkspacePathRequest(string Path);

public partial class Program;
