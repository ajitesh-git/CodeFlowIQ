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
builder.Services.AddSingleton<WorkspaceInventoryQueryHandler>();
builder.Services.AddSingleton<FlowChainQueryHandler>();
builder.Services.AddSingleton<RepositoryOverviewQueryHandler>();
builder.Services.AddSingleton<RuntimeFlowQueryHandler>();
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
        "/api/flows",
        "/api/chains",
        "/api/backend"
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

api.MapPost("/workspace/init", async (
    WorkspacePathRequest request,
    IWorkspaceIndexingService indexingService,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Path))
    {
        return Results.BadRequest(new { message = "Path is required." });
    }

    var summary = await indexingService.InitializeAsync(request.Path, cancellationToken);
    return Results.Ok(summary);
});

api.MapPost("/workspace/sync", async (
    WorkspacePathRequest request,
    IWorkspaceIndexingService indexingService,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Path))
    {
        return Results.BadRequest(new { message = "Path is required." });
    }

    var summary = await indexingService.SyncAsync(request.Path, cancellationToken);
    return Results.Ok(summary);
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
    value is > 0 and <= 1000 ? value.Value : defaultValue;

public sealed record WorkspacePathRequest(string Path);

public partial class Program;
