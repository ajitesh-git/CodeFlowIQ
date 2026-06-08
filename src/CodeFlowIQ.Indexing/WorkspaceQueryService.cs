using CodeFlowIQ.Core.Query;
using CodeFlowIQ.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CodeFlowIQ.Indexing;

public sealed class WorkspaceQueryService : IWorkspaceQueryService
{
    public async Task<WorkspaceStatus?> GetStatusAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var rootPath = Path.GetFullPath(workspacePath);
        await using var db = await WorkspaceDatabase.OpenMigratedAsync(rootPath, cancellationToken);

        var workspace = await db.Workspaces.FirstOrDefaultAsync(x => x.RootPath == rootPath, cancellationToken);
        if (workspace is null)
        {
            return null;
        }

        var fileCount = await db.IndexedFiles.CountAsync(x => x.WorkspaceId == workspace.Id && !x.IsDeleted, cancellationToken);
        var symbolCount = await db.CodeSymbols
            .Where(x => x.IndexedFile != null && x.IndexedFile.WorkspaceId == workspace.Id && !x.IndexedFile.IsDeleted)
            .CountAsync(cancellationToken);

        return new WorkspaceStatus(
            workspace.Name,
            workspace.RootPath,
            workspace.Kind.ToString(),
            workspace.CurrentBranch,
            workspace.HeadCommitSha,
            fileCount,
            symbolCount,
            workspace.LastIndexedAt);
    }

    public async Task<IReadOnlyList<string>> ListFilesAsync(string workspacePath, string? languageId, string? folderText, int take, CancellationToken cancellationToken)
    {
        var rootPath = Path.GetFullPath(workspacePath);
        await using var db = await WorkspaceDatabase.OpenMigratedAsync(rootPath, cancellationToken);

        var workspace = await db.Workspaces.FirstOrDefaultAsync(x => x.RootPath == rootPath, cancellationToken);
        if (workspace is null)
        {
            return [];
        }

        var query = db.IndexedFiles
            .Where(x => x.WorkspaceId == workspace.Id && !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(languageId))
        {
            query = query.Where(x => x.LanguageId == languageId);
        }

        if (!string.IsNullOrWhiteSpace(folderText) && !folderText.Equals("(root)", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => EF.Functions.Like(x.RelativePath, folderText + "\\%")
                || EF.Functions.Like(x.RelativePath, folderText + "/%")
                || EF.Functions.Like(x.RelativePath, $"%\\{folderText}\\%")
                || EF.Functions.Like(x.RelativePath, $"%/{folderText}/%"));
        }

        if (!string.IsNullOrWhiteSpace(folderText) && folderText.Equals("(root)", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => !EF.Functions.Like(x.RelativePath, "%\\%") && !EF.Functions.Like(x.RelativePath, "%/%"));
        }

        return await query
            .OrderBy(x => x.RelativePath)
            .Take(take)
            .Select(x => $"{x.LanguageId}\t{x.RelativePath}")
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> SearchSymbolsAsync(string workspacePath, string searchText, int take, CancellationToken cancellationToken)
    {
        var rootPath = Path.GetFullPath(workspacePath);
        await using var db = await WorkspaceDatabase.OpenMigratedAsync(rootPath, cancellationToken);

        var workspace = await db.Workspaces.FirstOrDefaultAsync(x => x.RootPath == rootPath, cancellationToken);
        if (workspace is null)
        {
            return [];
        }

        return await db.CodeSymbols
            .Where(x => x.IndexedFile != null
                && x.IndexedFile.WorkspaceId == workspace.Id
                && !x.IndexedFile.IsDeleted
                && EF.Functions.Like(x.Name, $"%{searchText}%"))
            .OrderBy(x => x.Name)
            .Take(take)
            .Select(x => $"{x.Kind}\t{x.Name}\t{x.IndexedFile!.RelativePath}:{x.LineNumber}")
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> SearchRelationshipsAsync(
        string workspacePath,
        string? searchText,
        string? relationshipKind,
        string? sourceText,
        string? targetText,
        bool includeTests,
        int take,
        CancellationToken cancellationToken)
    {
        var rootPath = Path.GetFullPath(workspacePath);
        await using var db = await WorkspaceDatabase.OpenMigratedAsync(rootPath, cancellationToken);

        var workspace = await db.Workspaces.FirstOrDefaultAsync(x => x.RootPath == rootPath, cancellationToken);
        if (workspace is null)
        {
            return [];
        }

        var query = db.CodeRelationships.Where(x => x.WorkspaceId == workspace.Id);
        query = ApplyRelationshipFilters(query, searchText, relationshipKind, sourceText, targetText, includeTests);

        return await query
            .OrderBy(x => x.SourceIdentifier)
            .ThenBy(x => x.RelationshipKind)
            .Take(take)
            .Select(x => $"{x.SourceKind}:{x.SourceIdentifier}\t{x.RelationshipKind}\t{x.TargetKind}:{x.TargetIdentifier}")
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListApisAsync(
        string workspacePath,
        string? httpMethod,
        string? routeText,
        string? controllerText,
        bool includeTests,
        int take,
        CancellationToken cancellationToken)
    {
        var rootPath = Path.GetFullPath(workspacePath);
        await using var db = await WorkspaceDatabase.OpenMigratedAsync(rootPath, cancellationToken);

        var workspace = await db.Workspaces.FirstOrDefaultAsync(x => x.RootPath == rootPath, cancellationToken);
        if (workspace is null)
        {
            return [];
        }

        var query = db.CodeRelationships
            .Where(x => x.WorkspaceId == workspace.Id && x.RelationshipKind == "handles_api");

        if (!includeTests)
        {
            query = ExcludeTestRelationships(query);
        }

        if (!string.IsNullOrWhiteSpace(httpMethod))
        {
            query = query.Where(x => EF.Functions.Like(x.TargetIdentifier, httpMethod.ToUpperInvariant() + " %"));
        }

        if (!string.IsNullOrWhiteSpace(routeText))
        {
            query = query.Where(x => EF.Functions.Like(x.TargetIdentifier, $"%{routeText}%"));
        }

        if (!string.IsNullOrWhiteSpace(controllerText))
        {
            query = query.Where(x =>
                EF.Functions.Like(x.SourceIdentifier, $"%{controllerText}%")
                || (x.Metadata != null && EF.Functions.Like(x.Metadata, $"%{controllerText}%")));
        }

        return await query
            .OrderBy(x => x.TargetIdentifier)
            .ThenBy(x => x.SourceIdentifier)
            .Take(take)
            .Select(x => $"{x.TargetIdentifier}\t{x.SourceIdentifier}\t{x.Metadata}")
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListAzureServicesAsync(
        string workspacePath,
        string? serviceText,
        bool includeTests,
        int take,
        CancellationToken cancellationToken)
    {
        var rootPath = Path.GetFullPath(workspacePath);
        await using var db = await WorkspaceDatabase.OpenMigratedAsync(rootPath, cancellationToken);

        var workspace = await db.Workspaces.FirstOrDefaultAsync(x => x.RootPath == rootPath, cancellationToken);
        if (workspace is null)
        {
            return [];
        }

        var query = db.CodeRelationships
            .Where(x => x.WorkspaceId == workspace.Id && x.RelationshipKind == "uses_azure_service");

        if (!includeTests)
        {
            query = ExcludeTestRelationships(query);
        }

        if (!string.IsNullOrWhiteSpace(serviceText))
        {
            query = query.Where(x => EF.Functions.Like(x.TargetIdentifier, $"%{serviceText}%"));
        }

        return await query
            .GroupBy(x => new { x.TargetIdentifier, x.SourceIdentifier })
            .OrderBy(x => x.Key.TargetIdentifier)
            .ThenBy(x => x.Key.SourceIdentifier)
            .Take(take)
            .Select(x => $"{x.Key.TargetIdentifier}\t{x.Key.SourceIdentifier}")
            .ToListAsync(cancellationToken);
    }

    public async Task<WorkspaceSummary?> GetSummaryAsync(string workspacePath, bool includeTests, int take, CancellationToken cancellationToken)
    {
        var rootPath = Path.GetFullPath(workspacePath);
        await using var db = await WorkspaceDatabase.OpenMigratedAsync(rootPath, cancellationToken);

        var workspace = await db.Workspaces.FirstOrDefaultAsync(x => x.RootPath == rootPath, cancellationToken);
        if (workspace is null)
        {
            return null;
        }

        var files = db.IndexedFiles.Where(x => x.WorkspaceId == workspace.Id && !x.IsDeleted);
        var symbols = db.CodeSymbols.Where(x => x.IndexedFile != null && x.IndexedFile.WorkspaceId == workspace.Id && !x.IndexedFile.IsDeleted);
        var relationships = db.CodeRelationships.Where(x => x.WorkspaceId == workspace.Id);

        if (!includeTests)
        {
            files = files.Where(x =>
                !EF.Functions.Like(x.RelativePath, "%\\Tests\\%")
                && !EF.Functions.Like(x.RelativePath, "%/Tests/%")
                && !EF.Functions.Like(x.RelativePath, "%.Tests\\%")
                && !EF.Functions.Like(x.RelativePath, "%.Tests/%")
                && !EF.Functions.Like(x.RelativePath, "%\\Test\\%")
                && !EF.Functions.Like(x.RelativePath, "%/Test/%"));
            symbols = symbols.Where(x => x.IndexedFile != null
                && !EF.Functions.Like(x.IndexedFile.RelativePath, "%\\Tests\\%")
                && !EF.Functions.Like(x.IndexedFile.RelativePath, "%/Tests/%")
                && !EF.Functions.Like(x.IndexedFile.RelativePath, "%.Tests\\%")
                && !EF.Functions.Like(x.IndexedFile.RelativePath, "%.Tests/%")
                && !EF.Functions.Like(x.IndexedFile.RelativePath, "%\\Test\\%")
                && !EF.Functions.Like(x.IndexedFile.RelativePath, "%/Test/%"));
            relationships = ExcludeTestRelationships(relationships);
        }

        var languageCounts = await files
            .GroupBy(x => x.LanguageId)
            .OrderByDescending(x => x.Count())
            .Take(take)
            .Select(x => $"{x.Key}\t{x.Count()}")
            .ToListAsync(cancellationToken);

        var symbolKindCounts = await symbols
            .GroupBy(x => x.Kind)
            .OrderByDescending(x => x.Count())
            .Take(take)
            .Select(x => $"{x.Key}\t{x.Count()}")
            .ToListAsync(cancellationToken);

        var relationshipKindCounts = await relationships
            .GroupBy(x => x.RelationshipKind)
            .OrderByDescending(x => x.Count())
            .Take(take)
            .Select(x => $"{x.Key}\t{x.Count()}")
            .ToListAsync(cancellationToken);

        var azureServiceCounts = await relationships
            .Where(x => x.RelationshipKind == "uses_azure_service")
            .GroupBy(x => x.TargetIdentifier)
            .OrderByDescending(x => x.Count())
            .Take(take)
            .Select(x => $"{x.Key}\t{x.Count()}")
            .ToListAsync(cancellationToken);

        return new WorkspaceSummary(
            workspace.Name,
            workspace.Kind.ToString(),
            await files.CountAsync(cancellationToken),
            await symbols.CountAsync(cancellationToken),
            await relationships.CountAsync(cancellationToken),
            languageCounts,
            symbolKindCounts,
            relationshipKindCounts,
            azureServiceCounts);
    }

    public async Task<RepositoryOverview?> GetRepositoryOverviewAsync(string workspacePath, bool includeTests, int take, CancellationToken cancellationToken)
    {
        var rootPath = Path.GetFullPath(workspacePath);
        await using var db = await WorkspaceDatabase.OpenMigratedAsync(rootPath, cancellationToken);

        var workspace = await db.Workspaces.FirstOrDefaultAsync(x => x.RootPath == rootPath, cancellationToken);
        if (workspace is null)
        {
            return null;
        }

        var normalizedTake = Math.Clamp(take, 1, 50);
        var files = db.IndexedFiles.Where(x => x.WorkspaceId == workspace.Id && !x.IsDeleted);
        var relationships = db.CodeRelationships.Where(x => x.WorkspaceId == workspace.Id);

        if (!includeTests)
        {
            files = ExcludeTestFiles(files);
            relationships = ExcludeTestRelationships(relationships);
        }

        var languageCounts = await files
            .GroupBy(x => x.LanguageId)
            .OrderByDescending(x => x.Count())
            .Take(normalizedTake)
            .Select(x => new RepositoryOverviewItem(
                FormatLanguageTitle(x.Key),
                $"{x.Count()} indexed files",
                "technology",
                x.Count()))
            .ToListAsync(cancellationToken);

        var importantFolders = await files
            .Select(x => x.RelativePath)
            .ToListAsync(cancellationToken);

        var folderItems = importantFolders
            .Select(GetTopLevelFolder)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(x => new RepositoryOverviewItem(
                x.Key,
                DescribeFolder(x.Key),
                "folder",
                x.Count()))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .Take(normalizedTake)
            .ToList();

        var apiItems = await relationships
            .Where(x => x.RelationshipKind == "handles_api")
            .GroupBy(x => new { x.TargetIdentifier, x.SourceIdentifier })
            .OrderBy(x => x.Key.TargetIdentifier)
            .Take(normalizedTake)
            .Select(x => new RepositoryOverviewItem(
                x.Key.TargetIdentifier,
                $"Handled by {x.Key.SourceIdentifier}",
                "api",
                x.Count()))
            .ToListAsync(cancellationToken);

        var flowRows = await relationships
            .Where(x => x.RelationshipKind == "matches_backend_handler")
            .OrderBy(x => x.Metadata)
            .ThenBy(x => x.SourceIdentifier)
            .Take(normalizedTake * 3)
            .ToListAsync(cancellationToken);

        var flowItems = flowRows
            .GroupBy(x => NormalizeFlowName(x.Metadata ?? x.TargetIdentifier), StringComparer.OrdinalIgnoreCase)
            .Select(x =>
            {
                var first = x.First();
                return new RepositoryOverviewItem(
                    x.Key,
                    $"{FormatIdentifier(first.SourceIdentifier)} connects to {FormatIdentifier(first.TargetIdentifier)}",
                    "flow",
                    x.Count());
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .Take(normalizedTake)
            .ToList();

        var dataItems = await relationships
            .Where(x => x.RelationshipKind == "reads_table"
                || x.RelationshipKind == "writes_table"
                || x.RelationshipKind == "executes_procedure")
            .GroupBy(x => new { x.TargetKind, x.TargetIdentifier })
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key.TargetIdentifier)
            .Take(normalizedTake)
            .Select(x => new RepositoryOverviewItem(
                FormatIdentifier(x.Key.TargetIdentifier),
                $"{x.Key.TargetKind} used by {x.Count()} relationships",
                "data",
                x.Count()))
            .ToListAsync(cancellationToken);

        var azureItems = await relationships
            .Where(x => x.RelationshipKind == "uses_azure_service")
            .GroupBy(x => x.TargetIdentifier)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key)
            .Take(normalizedTake)
            .Select(x => new RepositoryOverviewItem(
                FormatIdentifier(x.Key),
                $"{x.Count()} usages detected",
                "azure",
                x.Count()))
            .ToListAsync(cancellationToken);

        var startingPoints = BuildStartingPoints(apiItems, flowItems, dataItems, azureItems, folderItems, normalizedTake);
        var summary = BuildOverviewSummary(workspace.Kind.ToString(), languageCounts, apiItems, flowItems, dataItems, azureItems);

        return new RepositoryOverview(
            workspace.Name,
            workspace.Kind.ToString(),
            summary,
            languageCounts,
            startingPoints,
            flowItems,
            apiItems,
            dataItems,
            azureItems,
            folderItems);
    }

    public async Task<RuntimeFlowMap?> GetRuntimeFlowMapAsync(string workspacePath, bool includeTests, int take, CancellationToken cancellationToken)
    {
        var rootPath = Path.GetFullPath(workspacePath);
        await using var db = await WorkspaceDatabase.OpenMigratedAsync(rootPath, cancellationToken);

        var workspace = await db.Workspaces.FirstOrDefaultAsync(x => x.RootPath == rootPath, cancellationToken);
        if (workspace is null)
        {
            return null;
        }

        var normalizedTake = Math.Clamp(take, 1, 50);
        var fileQuery = db.IndexedFiles.Where(x => x.WorkspaceId == workspace.Id && !x.IsDeleted);
        var relationshipQuery = db.CodeRelationships.Where(x => x.WorkspaceId == workspace.Id);

        if (!includeTests)
        {
            fileQuery = ExcludeTestFiles(fileQuery);
            relationshipQuery = ExcludeTestRelationships(relationshipQuery);
        }

        var files = await fileQuery
            .Select(x => new RuntimeFile(x.RelativePath, x.LanguageId))
            .ToListAsync(cancellationToken);
        var relationships = await relationshipQuery.ToListAsync(cancellationToken);

        var entryPoints = BuildRuntimeEntryPoints(files, relationships, normalizedTake);
        var flows = BuildRuntimeFlows(relationships, normalizedTake);
        var executionPaths = BuildRuntimeExecutionPaths(entryPoints, flows, relationships, normalizedTake);
        var summary = BuildRuntimeFlowSummary(entryPoints, flows);

        return new RuntimeFlowMap(
            workspace.Name,
            workspace.Kind.ToString(),
            summary,
            entryPoints,
            executionPaths,
            flows);
    }

    public async Task<IReadOnlyList<string>> ListFlowsAsync(
        string workspacePath,
        string? apiText,
        string? sourceText,
        string? handlerText,
        bool includeTests,
        int take,
        CancellationToken cancellationToken)
    {
        var rootPath = Path.GetFullPath(workspacePath);
        await using var db = await WorkspaceDatabase.OpenMigratedAsync(rootPath, cancellationToken);

        var workspace = await db.Workspaces.FirstOrDefaultAsync(x => x.RootPath == rootPath, cancellationToken);
        if (workspace is null)
        {
            return [];
        }

        var query = db.CodeRelationships
            .Where(x => x.WorkspaceId == workspace.Id && x.RelationshipKind == "matches_backend_handler");

        if (!includeTests)
        {
            query = ExcludeTestRelationships(query);
        }

        if (!string.IsNullOrWhiteSpace(apiText))
        {
            query = query.Where(x => x.Metadata != null && EF.Functions.Like(x.Metadata, $"%{apiText}%"));
        }

        if (!string.IsNullOrWhiteSpace(sourceText))
        {
            query = query.Where(x => EF.Functions.Like(x.SourceIdentifier, $"%{sourceText}%"));
        }

        if (!string.IsNullOrWhiteSpace(handlerText))
        {
            query = query.Where(x => EF.Functions.Like(x.TargetIdentifier, $"%{handlerText}%"));
        }

        return await query
            .OrderBy(x => x.SourceIdentifier)
            .ThenBy(x => x.TargetIdentifier)
            .Take(take)
            .Select(x => $"{x.SourceKind}:{x.SourceIdentifier}\tmatches_backend_handler\t{x.TargetKind}:{x.TargetIdentifier}\t{x.Metadata}")
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListFlowChainsAsync(
        string workspacePath,
        string? apiText,
        string? sourceText,
        string? targetText,
        string? format,
        bool includeTests,
        int maxDepth,
        int take,
        CancellationToken cancellationToken)
    {
        var rootPath = Path.GetFullPath(workspacePath);
        await using var db = await WorkspaceDatabase.OpenMigratedAsync(rootPath, cancellationToken);

        var workspace = await db.Workspaces.FirstOrDefaultAsync(x => x.RootPath == rootPath, cancellationToken);
        if (workspace is null)
        {
            return [];
        }

        var relationshipQuery = db.CodeRelationships.Where(x => x.WorkspaceId == workspace.Id);
        if (!includeTests)
        {
            relationshipQuery = ExcludeTestRelationships(relationshipQuery);
        }

        var relationships = await relationshipQuery.ToListAsync(cancellationToken);
        var starts = relationships
            .Where(x => x.RelationshipKind == "matches_backend_handler")
            .Where(x => string.IsNullOrWhiteSpace(apiText) || (x.Metadata is not null && x.Metadata.Contains(apiText, StringComparison.OrdinalIgnoreCase)))
            .Where(x => string.IsNullOrWhiteSpace(sourceText) || x.SourceIdentifier.Contains(sourceText, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.SourceIdentifier)
            .ThenBy(x => x.TargetIdentifier)
            .Take(Math.Max(take * 4, take))
            .ToList();

        if (starts.Count == 0)
        {
            starts = relationships
                .Where(x => x.RelationshipKind == "handled_by")
                .Where(x => string.IsNullOrWhiteSpace(apiText) || x.SourceIdentifier.Contains(apiText, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.SourceIdentifier)
                .ThenBy(x => x.TargetIdentifier)
                .Take(Math.Max(take * 4, take))
                .ToList();
        }

        var graph = FlowGraph.Create(relationships);
        var chains = new List<string>();
        foreach (var start in starts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var seed = new FlowStep(start.SourceKind, start.SourceIdentifier, start.RelationshipKind, start.TargetKind, start.TargetIdentifier);
            foreach (var chain in graph.Expand(seed, maxDepth))
            {
                var searchable = FormatFlowChain(chain, "compact");
                if (!string.IsNullOrWhiteSpace(targetText)
                    && !searchable.Contains(targetText, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                chains.Add(FormatFlowChain(chain, format));
                if (chains.Count >= take)
                {
                    return chains;
                }
            }
        }

        return chains;
    }

    private static IQueryable<Core.Models.CodeRelationship> ApplyRelationshipFilters(
        IQueryable<Core.Models.CodeRelationship> query,
        string? searchText,
        string? relationshipKind,
        string? sourceText,
        string? targetText,
        bool includeTests)
    {
        if (!includeTests)
        {
            query = ExcludeTestRelationships(query);
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            query = query.Where(x =>
                EF.Functions.Like(x.SourceIdentifier, $"%{searchText}%")
                || EF.Functions.Like(x.TargetIdentifier, $"%{searchText}%")
                || EF.Functions.Like(x.RelationshipKind, $"%{searchText}%"));
        }

        if (!string.IsNullOrWhiteSpace(relationshipKind))
        {
            query = query.Where(x => EF.Functions.Like(x.RelationshipKind, $"%{relationshipKind}%"));
        }

        if (!string.IsNullOrWhiteSpace(sourceText))
        {
            query = query.Where(x => EF.Functions.Like(x.SourceIdentifier, $"%{sourceText}%"));
        }

        if (!string.IsNullOrWhiteSpace(targetText))
        {
            query = query.Where(x => EF.Functions.Like(x.TargetIdentifier, $"%{targetText}%"));
        }

        return query;
    }

    private static IQueryable<Core.Models.CodeRelationship> ExcludeTestRelationships(IQueryable<Core.Models.CodeRelationship> query) =>
        query.Where(x =>
            !EF.Functions.Like(x.SourceIdentifier, "%\\Tests\\%")
            && !EF.Functions.Like(x.SourceIdentifier, "%/Tests/%")
            && !EF.Functions.Like(x.SourceIdentifier, "%.Tests\\%")
            && !EF.Functions.Like(x.SourceIdentifier, "%.Tests/%")
            && !EF.Functions.Like(x.SourceIdentifier, "%\\Test\\%")
            && !EF.Functions.Like(x.SourceIdentifier, "%/Test/%")
            && !EF.Functions.Like(x.TargetIdentifier, "%\\Tests\\%")
            && !EF.Functions.Like(x.TargetIdentifier, "%/Tests/%")
            && !EF.Functions.Like(x.TargetIdentifier, "%.Tests\\%")
            && !EF.Functions.Like(x.TargetIdentifier, "%.Tests/%")
            && !EF.Functions.Like(x.TargetIdentifier, "%\\Test\\%")
            && !EF.Functions.Like(x.TargetIdentifier, "%/Test/%"));

    private static IQueryable<Core.Models.IndexedFile> ExcludeTestFiles(IQueryable<Core.Models.IndexedFile> query) =>
        query.Where(x =>
            !EF.Functions.Like(x.RelativePath, "%\\Tests\\%")
            && !EF.Functions.Like(x.RelativePath, "%/Tests/%")
            && !EF.Functions.Like(x.RelativePath, "%.Tests\\%")
            && !EF.Functions.Like(x.RelativePath, "%.Tests/%")
            && !EF.Functions.Like(x.RelativePath, "%\\Test\\%")
            && !EF.Functions.Like(x.RelativePath, "%/Test/%"));

    private static IReadOnlyList<RuntimeEntryPoint> BuildRuntimeEntryPoints(
        IReadOnlyList<RuntimeFile> files,
        IReadOnlyList<Core.Models.CodeRelationship> relationships,
        int take)
    {
        var entries = new List<RuntimeEntryPoint>();

        foreach (var file in files)
        {
            var path = file.RelativePath.Replace('\\', '/');
            var fileName = Path.GetFileName(path);
            if (fileName.Equals("main.ts", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("main.tsx", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("index.tsx", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("index.jsx", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("App.tsx", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("App.jsx", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("app.module.ts", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("app-routing.module.ts", StringComparison.OrdinalIgnoreCase))
            {
                entries.Add(new RuntimeEntryPoint(
                    FormatIdentifier(path),
                    path,
                    "frontend-startup",
                    fileName.Contains("routing", StringComparison.OrdinalIgnoreCase) ? 90 : 80));
            }

            if (fileName.Equals("Program.cs", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("Startup.cs", StringComparison.OrdinalIgnoreCase))
            {
                entries.Add(new RuntimeEntryPoint(
                    FormatIdentifier(path),
                    path,
                    "backend-startup",
                    90));
            }

            if (fileName.Equals("host.json", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("function.json", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("local.settings.json", StringComparison.OrdinalIgnoreCase))
            {
                entries.Add(new RuntimeEntryPoint(
                    FormatIdentifier(path),
                    path,
                    "function-worker",
                    85));
            }
        }

        var apiCount = relationships.Count(x => x.RelationshipKind == "handles_api");
        if (apiCount > 0)
        {
            entries.Add(new RuntimeEntryPoint(
                "HTTP API surface",
                $"{apiCount} backend API handlers receive outside requests.",
                "api-entry",
                Math.Min(95, 65 + apiCount)));
        }

        var frontendCallCount = relationships.Count(x => x.RelationshipKind == "matches_backend_handler");
        if (frontendCallCount > 0)
        {
            entries.Add(new RuntimeEntryPoint(
                "Frontend-to-backend interactions",
                $"{frontendCallCount} client calls were matched to backend handlers.",
                "cross-stack-entry",
                Math.Min(95, 70 + frontendCallCount)));
        }

        return entries
            .OrderByDescending(x => x.Confidence)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToList();
    }

    private static IReadOnlyList<RuntimeFlow> BuildRuntimeFlows(
        IReadOnlyList<Core.Models.CodeRelationship> relationships,
        int take)
    {
        var flows = new List<RuntimeFlow>();
        var crossStackFlows = relationships
            .Where(x => x.RelationshipKind == "matches_backend_handler")
            .OrderBy(x => x.Metadata)
            .ThenBy(x => x.SourceIdentifier)
            .Take(take * 2)
            .ToList();

        foreach (var relationship in crossStackFlows)
        {
            var api = ExtractMetadataValue(relationship.Metadata ?? string.Empty, "backendApi")
                ?? ExtractMetadataValue(relationship.Metadata ?? string.Empty, "frontendApi")
                ?? "Matched API call";
            var steps = new List<RuntimeFlowStep>();
            steps.AddRange(BuildUpstreamRuntimeSteps(relationships, relationship.SourceIdentifier, 4));
            steps.AddRange(new RuntimeFlowStep[]
            {
                new("Frontend", FormatIdentifier(relationship.SourceIdentifier), relationship.SourceIdentifier, relationship.SourceKind),
                new("HTTP API", api, "Client request crosses into backend API surface.", "api"),
                new("Backend", FormatIdentifier(relationship.TargetIdentifier), relationship.TargetIdentifier, relationship.TargetKind)
            });
            steps.AddRange(BuildDownstreamRuntimeSteps(relationships, relationship.TargetIdentifier, 4));
            steps.AddRange(BuildRelatedNavigationSteps(relationships, steps, relationship.SourceIdentifier));

            flows.Add(new RuntimeFlow(
                NormalizeFlowName(api),
                $"{FormatIdentifier(relationship.SourceIdentifier)} reaches {FormatIdentifier(relationship.TargetIdentifier)}.",
                "cross-stack",
                Math.Min(95, 70 + steps.Count * 4),
                steps));
        }

        var usedBackendHandlers = new HashSet<string>(crossStackFlows.Select(x => x.TargetIdentifier), StringComparer.OrdinalIgnoreCase);
        var apiOnlyFlows = relationships
            .Where(x => x.RelationshipKind == "handles_api" && !usedBackendHandlers.Contains(x.SourceIdentifier))
            .OrderBy(x => x.TargetIdentifier)
            .Take(Math.Max(0, take - flows.Count))
            .ToList();

        foreach (var relationship in apiOnlyFlows)
        {
            var steps = new List<RuntimeFlowStep>
            {
                new("HTTP API", relationship.TargetIdentifier, "Outside request enters the backend here.", "api"),
                new("Backend", FormatIdentifier(relationship.SourceIdentifier), relationship.SourceIdentifier, relationship.SourceKind)
            };
            steps.AddRange(BuildDownstreamRuntimeSteps(relationships, relationship.SourceIdentifier, 4));

            flows.Add(new RuntimeFlow(
                NormalizeFlowName(relationship.TargetIdentifier),
                $"{relationship.TargetIdentifier} is handled by {FormatIdentifier(relationship.SourceIdentifier)}.",
                "backend-api",
                Math.Min(90, 60 + steps.Count * 4),
                steps));
        }

        var uiOnlyFlows = relationships
            .Where(x => x.RelationshipKind == "invokes_handler")
            .Where(x => !flows.Any(flow => flow.Steps.Any(step => step.Detail.Equals(x.TargetIdentifier, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(x => x.SourceIdentifier)
            .Take(Math.Max(0, take - flows.Count))
            .ToList();

        foreach (var relationship in uiOnlyFlows)
        {
            var steps = new List<RuntimeFlowStep>
            {
                new("UI event", FormatIdentifier(relationship.SourceIdentifier), relationship.SourceIdentifier, relationship.SourceKind),
                new("Frontend handler", FormatIdentifier(relationship.TargetIdentifier), relationship.TargetIdentifier, relationship.TargetKind)
            };
            steps.AddRange(BuildDownstreamRuntimeSteps(relationships, relationship.TargetIdentifier, 4));
            steps.AddRange(BuildRelatedNavigationSteps(relationships, steps, relationship.TargetIdentifier));

            flows.Add(new RuntimeFlow(
                FormatIdentifier(relationship.TargetIdentifier),
                $"{FormatIdentifier(relationship.SourceIdentifier)} invokes {FormatIdentifier(relationship.TargetIdentifier)}.",
                "ui-interaction",
                Math.Min(82, 54 + steps.Count * 4),
                steps));
        }

        var routeFlows = relationships
            .Where(x => x.RelationshipKind == "renders_component")
            .OrderBy(x => x.SourceIdentifier)
            .Take(Math.Max(0, take - flows.Count))
            .ToList();

        foreach (var relationship in routeFlows)
        {
            flows.Add(new RuntimeFlow(
                relationship.SourceIdentifier,
                $"{relationship.SourceIdentifier} renders {FormatIdentifier(relationship.TargetIdentifier)}.",
                "frontend-route",
                62,
                [
                    new("Route", relationship.SourceIdentifier, "Client-side route selected by the frontend router.", relationship.SourceKind),
                    new("Page/component", FormatIdentifier(relationship.TargetIdentifier), relationship.TargetIdentifier, relationship.TargetKind)
                ]));
        }

        return flows
            .OrderByDescending(x => x.Confidence)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToList();
    }

    private static IReadOnlyList<RuntimeExecutionPath> BuildRuntimeExecutionPaths(
        IReadOnlyList<RuntimeEntryPoint> entryPoints,
        IReadOnlyList<RuntimeFlow> flows,
        IReadOnlyList<Core.Models.CodeRelationship> relationships,
        int take)
    {
        var paths = new List<RuntimeExecutionPath>();

        foreach (var entryPoint in entryPoints)
        {
            var relatedFlows = flows
                .Where(flow => RuntimeFlowBelongsToEntryPoint(entryPoint, flow))
                .OrderByDescending(flow => flow.Confidence)
                .ThenBy(flow => flow.Title, StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList();

            if (relatedFlows.Count == 0)
            {
                relatedFlows.AddRange(BuildBackendStartupDataFlows(entryPoint, relationships, 6));
            }

            if (relatedFlows.Count == 0)
            {
                relatedFlows.Add(new RuntimeFlow(
                    entryPoint.Title,
                    "CodeFlowIQ detected this runtime entry point, but no deeper executable path has been connected yet.",
                    entryPoint.Category,
                    entryPoint.Confidence,
                    [
                        new("Entry point", entryPoint.Title, entryPoint.Detail, entryPoint.Category)
                    ]));
            }

            paths.Add(new RuntimeExecutionPath(
                entryPoint.Title,
                entryPoint.Detail,
                entryPoint.Category,
                BuildExecutionPathSummary(entryPoint, relatedFlows),
                relatedFlows));
        }

        return paths
            .OrderByDescending(x => x.Flows.Max(flow => flow.Confidence))
            .ThenBy(x => x.EntryPointTitle, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToList();
    }

    private static bool RuntimeFlowBelongsToEntryPoint(RuntimeEntryPoint entryPoint, RuntimeFlow flow)
    {
        if (entryPoint.Category == "cross-stack-entry")
        {
            return flow.Category is "cross-stack" or "ui-interaction" or "frontend-route";
        }

        if (entryPoint.Category == "api-entry")
        {
            return flow.Category is "backend-api" or "cross-stack";
        }

        if (entryPoint.Category == "frontend-startup")
        {
            return flow.Category is "frontend-route" or "ui-interaction" or "cross-stack"
                && RuntimeFlowTouchesEntryArea(entryPoint, flow);
        }

        if (entryPoint.Category == "backend-startup")
        {
            return flow.Category is "backend-api" or "cross-stack"
                && RuntimeFlowTouchesEntryArea(entryPoint, flow);
        }

        if (entryPoint.Category == "function-worker")
        {
            return flow.Steps.Any(step =>
                step.Kind.Contains("function", StringComparison.OrdinalIgnoreCase)
                || step.Detail.Contains("Function", StringComparison.OrdinalIgnoreCase))
                && RuntimeFlowTouchesEntryArea(entryPoint, flow);
        }

        return RuntimeFlowTouchesEntryArea(entryPoint, flow);
    }

    private static IReadOnlyList<RuntimeFlow> BuildBackendStartupDataFlows(
        RuntimeEntryPoint entryPoint,
        IReadOnlyList<Core.Models.CodeRelationship> relationships,
        int take)
    {
        if (entryPoint.Category != "backend-startup" && entryPoint.Category != "function-worker")
        {
            return [];
        }

        var domainTokens = GetRuntimeStartupDomainTokens(entryPoint.Detail);
        if (domainTokens.Count == 0)
        {
            return [];
        }

        var dataKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            "executes_procedure",
            "reads_table",
            "writes_table",
            "saves_changes"
        };

        var seeds = relationships
            .Where(x => dataKinds.Contains(x.RelationshipKind))
            .Where(x => RuntimeDataRelationshipMatchesStartupDomain(x, domainTokens))
            .GroupBy(x => $"{x.SourceIdentifier}|{x.RelationshipKind}|{x.TargetIdentifier}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => GetRuntimeStepRank(x.RelationshipKind))
            .ThenBy(x => x.SourceIdentifier)
            .ThenBy(x => x.TargetIdentifier)
            .Take(take)
            .ToList();

        var flows = new List<RuntimeFlow>();
        foreach (var seed in seeds)
        {
            var steps = new List<RuntimeFlowStep>
            {
                new("Entry point", entryPoint.Title, entryPoint.Detail, entryPoint.Category),
                new("Backend", FormatIdentifier(seed.SourceIdentifier), seed.SourceIdentifier, seed.SourceKind),
                new(StageForRelationship(seed.RelationshipKind), FormatIdentifier(seed.TargetIdentifier), $"{FormatRelationship(seed.RelationshipKind)} from {FormatIdentifier(seed.SourceIdentifier)}.", seed.TargetKind)
            };
            steps.AddRange(BuildDownstreamRuntimeSteps(relationships, seed.TargetIdentifier, 4));

            flows.Add(new RuntimeFlow(
                $"{FormatIdentifier(seed.SourceIdentifier)} to {FormatIdentifier(seed.TargetIdentifier)}",
                $"{entryPoint.Title} is associated with {FormatIdentifier(seed.SourceIdentifier)}, which {FormatRelationship(seed.RelationshipKind).ToLowerInvariant()} {FormatIdentifier(seed.TargetIdentifier)}.",
                "backend-data",
                Math.Min(88, 58 + steps.Count * 5),
                DeduplicateRuntimeSteps(steps)));
        }

        return flows
            .OrderByDescending(x => x.Confidence)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool RuntimeFlowTouchesEntryArea(RuntimeEntryPoint entryPoint, RuntimeFlow flow)
    {
        var entryPath = NormalizeRuntimeIdentifierPath(entryPoint.Detail);
        if (string.IsNullOrWhiteSpace(entryPath))
        {
            return false;
        }

        var entryArea = GetRuntimeEntryArea(entryPath);
        return flow.Steps.Any(step =>
        {
            var stepPath = NormalizeRuntimeIdentifierPath(step.Detail);
            return stepPath.Contains(entryPath, StringComparison.OrdinalIgnoreCase)
                || entryPath.Contains(stepPath, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(entryArea)
                    && stepPath.Contains(entryArea, StringComparison.OrdinalIgnoreCase));
        });
    }

    private static string NormalizeRuntimeIdentifierPath(string identifier)
    {
        var path = identifier.Split("::", StringSplitOptions.TrimEntries)[0]
            .Replace('\\', '/')
            .Trim();
        return path;
    }

    private static string GetRuntimeEntryArea(string entryPath)
    {
        var directory = Path.GetDirectoryName(entryPath)?.Replace('\\', '/') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(directory))
        {
            return string.Empty;
        }

        var fileName = Path.GetFileName(entryPath);
        if (fileName.Equals("Program.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("Startup.cs", StringComparison.OrdinalIgnoreCase))
        {
            return directory;
        }

        var appSegment = "/src/app/";
        var appIndex = directory.IndexOf(appSegment, StringComparison.OrdinalIgnoreCase);
        if (appIndex >= 0)
        {
            return directory[..(appIndex + appSegment.Length - 1)];
        }

        return directory;
    }

    private static IReadOnlyList<string> GetRuntimeStartupDomainTokens(string entryDetail)
    {
        var normalized = NormalizeRuntimeIdentifierPath(entryDetail);
        var fileName = Path.GetFileName(normalized);
        var directory = Path.GetDirectoryName(normalized)?.Replace('\\', '/') ?? normalized;
        var projectName = directory
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(x => x.Contains('.', StringComparison.Ordinal))
            ?? directory;

        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Deloitte",
            "Omnia",
            "FinancialFacts",
            "Api",
            "Functions",
            "Function",
            "Program",
            "Startup"
        };

        var rawParts = projectName
            .Replace(fileName, string.Empty, StringComparison.OrdinalIgnoreCase)
            .Split(['.', '-', '_', '/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !ignored.Contains(x))
            .SelectMany(x => new[] { x }.Concat(SplitPascalCaseWords(x)))
            .Where(x => x.Length >= 4 && !ignored.Contains(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return rawParts;
    }

    private static IEnumerable<string> SplitPascalCaseWords(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        var start = 0;
        for (var index = 1; index < value.Length; index++)
        {
            if (char.IsUpper(value[index]) && !char.IsUpper(value[index - 1]))
            {
                yield return value[start..index];
                start = index;
            }
        }

        yield return value[start..];
    }

    private static bool RuntimeDataRelationshipMatchesStartupDomain(
        Core.Models.CodeRelationship relationship,
        IReadOnlyList<string> domainTokens)
    {
        var searchable = $"{relationship.SourceIdentifier} {relationship.TargetIdentifier} {relationship.Metadata}";
        return domainTokens.Any(token => searchable.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<RuntimeFlowStep> DeduplicateRuntimeSteps(IEnumerable<RuntimeFlowStep> steps)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduped = new List<RuntimeFlowStep>();
        foreach (var step in steps)
        {
            var key = $"{step.Stage}|{step.Title}|{step.Detail}|{step.Kind}";
            if (seen.Add(key))
            {
                deduped.Add(step);
            }
        }

        return deduped;
    }

    private static string BuildExecutionPathSummary(RuntimeEntryPoint entryPoint, IReadOnlyList<RuntimeFlow> flows)
    {
        if (flows.Count == 1 && flows[0].Steps.Count == 1)
        {
            return "Detected entry point only. More analyzer signals are needed to connect the next runtime step.";
        }

        var totalSteps = flows.Sum(flow => flow.Steps.Count);
        return $"{entryPoint.Title} connects to {flows.Count} flow paths with {totalSteps} ordered runtime steps.";
    }

    private static IReadOnlyList<RuntimeFlowStep> BuildDownstreamRuntimeSteps(
        IReadOnlyList<Core.Models.CodeRelationship> relationships,
        string sourceIdentifier,
        int maxSteps)
    {
        var downstreamKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            "calls_method",
            "calls_api",
            "handled_by",
            "navigates_to",
            "executes_procedure",
            "reads_table",
            "writes_table",
            "saves_changes",
            "uses_azure_service"
        };
        var steps = new List<RuntimeFlowStep>();
        var currentSource = sourceIdentifier;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { sourceIdentifier };

        while (steps.Count < maxSteps)
        {
            var next = relationships
                .Where(x => x.SourceIdentifier.Equals(currentSource, StringComparison.OrdinalIgnoreCase)
                    && downstreamKinds.Contains(x.RelationshipKind))
                .OrderBy(x => GetRuntimeStepRank(x.RelationshipKind))
                .ThenBy(x => x.TargetIdentifier)
                .FirstOrDefault();

            next ??= ResolveRuntimeSourceRelationships(relationships, currentSource, downstreamKinds)
                .OrderBy(x => GetRuntimeStepRank(x.RelationshipKind))
                .ThenBy(x => x.TargetIdentifier)
                .FirstOrDefault();

            if (next is null || !visited.Add(next.TargetIdentifier))
            {
                break;
            }

            steps.Add(new RuntimeFlowStep(
                StageForRelationship(next.RelationshipKind),
                FormatIdentifier(next.TargetIdentifier),
                $"{FormatRelationship(next.RelationshipKind)} from {FormatIdentifier(next.SourceIdentifier)}.",
                next.TargetKind));
            currentSource = next.TargetIdentifier;
        }

        return steps;
    }

    private static IReadOnlyList<RuntimeFlowStep> BuildRelatedNavigationSteps(
        IReadOnlyList<Core.Models.CodeRelationship> relationships,
        IReadOnlyList<RuntimeFlowStep> currentSteps,
        string sourceIdentifier)
    {
        var sourceCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { sourceIdentifier };
        foreach (var step in currentSteps)
        {
            if (!string.IsNullOrWhiteSpace(step.Detail))
            {
                sourceCandidates.Add(step.Detail);
            }
        }

        return relationships
            .Where(x => x.RelationshipKind == "navigates_to" && sourceCandidates.Contains(x.SourceIdentifier))
            .OrderBy(x => x.TargetIdentifier)
            .Take(3)
            .Select(x => new RuntimeFlowStep(
                "Navigation outcome",
                x.TargetIdentifier,
                $"{FormatIdentifier(x.SourceIdentifier)} navigates to this client route.",
                x.TargetKind))
            .ToList();
    }

    private static IReadOnlyList<RuntimeFlowStep> BuildUpstreamRuntimeSteps(
        IReadOnlyList<Core.Models.CodeRelationship> relationships,
        string sourceIdentifier,
        int maxSteps)
    {
        var steps = new List<RuntimeFlowStep>();
        var currentTarget = sourceIdentifier;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { sourceIdentifier };

        while (steps.Count < maxSteps)
        {
            var previous = relationships
                .Where(x => (x.RelationshipKind == "invokes_handler" || x.RelationshipKind == "calls_method")
                    && RuntimeTargetsMatch(x.TargetIdentifier, currentTarget))
                .OrderBy(x => x.RelationshipKind == "invokes_handler" ? 0 : 1)
                .ThenBy(x => x.SourceIdentifier)
                .FirstOrDefault();

            if (previous is null || !visited.Add(previous.SourceIdentifier))
            {
                break;
            }

            steps.Insert(0, new RuntimeFlowStep(
                StageForRelationship(previous.RelationshipKind),
                FormatIdentifier(previous.SourceIdentifier),
                $"{FormatRelationship(previous.RelationshipKind)} into {FormatIdentifier(previous.TargetIdentifier)}.",
                previous.SourceKind));
            currentTarget = previous.SourceIdentifier;
        }

        var route = relationships
            .Where(x => x.RelationshipKind == "renders_component"
                && ComponentRouteMatches(x.TargetIdentifier, steps.FirstOrDefault()?.Detail ?? sourceIdentifier))
            .OrderBy(x => x.SourceIdentifier)
            .FirstOrDefault();
        if (route is not null)
        {
            steps.Insert(0, new RuntimeFlowStep(
                "Route",
                route.SourceIdentifier,
                $"Route renders {FormatIdentifier(route.TargetIdentifier)}.",
                route.SourceKind));
        }

        return steps;
    }

    private static IEnumerable<Core.Models.CodeRelationship> ResolveRuntimeSourceRelationships(
        IReadOnlyList<Core.Models.CodeRelationship> relationships,
        string currentSource,
        IReadOnlySet<string> downstreamKinds)
    {
        var memberName = GetRuntimeMemberName(currentSource);
        if (string.IsNullOrWhiteSpace(memberName))
        {
            yield break;
        }

        foreach (var relationship in relationships.Where(x =>
            downstreamKinds.Contains(x.RelationshipKind)
            && GetRuntimeMemberName(x.SourceIdentifier).Equals(memberName, StringComparison.OrdinalIgnoreCase)))
        {
            yield return relationship;
        }
    }

    private static bool RuntimeTargetsMatch(string candidateTarget, string currentTarget)
    {
        if (candidateTarget.Equals(currentTarget, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var candidateMember = GetRuntimeMemberName(candidateTarget);
        var currentMember = GetRuntimeMemberName(currentTarget);
        return candidateMember.Length > 0
            && currentMember.Length > 0
            && candidateMember.Equals(currentMember, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ComponentRouteMatches(string componentIdentifier, string runtimeIdentifier)
    {
        var componentName = GetRuntimeMemberName(componentIdentifier);
        if (string.IsNullOrWhiteSpace(componentName))
        {
            return false;
        }

        var inferredComponent = InferComponentNameFromPath(runtimeIdentifier);
        return componentName.Equals(inferredComponent, StringComparison.OrdinalIgnoreCase)
            || runtimeIdentifier.Contains(componentName, StringComparison.OrdinalIgnoreCase);
    }

    private static string InferComponentNameFromPath(string identifier)
    {
        var path = identifier.Split("::", StringSplitOptions.TrimEntries)[0];
        var fileName = Path.GetFileName(path);
        var stem = fileName
            .Replace(".component.ts", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(".component.html", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(".tsx", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(".jsx", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(".ts", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(".js", string.Empty, StringComparison.OrdinalIgnoreCase);

        return string.Concat(stem
            .Split(['-', '_', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => $"{char.ToUpperInvariant(x[0])}{x[1..]}")) + "Component";
    }

    private static string GetRuntimeMemberName(string identifier)
    {
        var qualifiedIndex = identifier.LastIndexOf("::", StringComparison.Ordinal);
        var member = qualifiedIndex >= 0 ? identifier[(qualifiedIndex + 2)..] : identifier;
        var dotIndex = member.LastIndexOf(".", StringComparison.Ordinal);
        return dotIndex >= 0 && dotIndex < member.Length - 1 ? member[(dotIndex + 1)..] : member;
    }

    private static string BuildRuntimeFlowSummary(
        IReadOnlyList<RuntimeEntryPoint> entryPoints,
        IReadOnlyList<RuntimeFlow> flows)
    {
        if (entryPoints.Count == 0 && flows.Count == 0)
        {
            return "Runtime flow discovery needs more indexed entry signals. Initialize or sync the workspace to improve this map.";
        }

        return $"CodeFlowIQ found {entryPoints.Count} likely runtime entry points and {flows.Count} runnable flow paths from the local index.";
    }

    private static int GetRuntimeStepRank(string relationshipKind) =>
        relationshipKind switch
        {
            "executes_procedure" => 10,
            "writes_table" => 12,
            "reads_table" => 14,
            "calls_method" => 20,
            "calls_api" => 22,
            "handled_by" => 24,
            "navigates_to" => 25,
            "uses_azure_service" => 20,
            "saves_changes" => 60,
            _ => 100
        };

    private static string StageForRelationship(string relationshipKind) =>
        relationshipKind switch
        {
            "calls_method" => "Backend call",
            "calls_api" => "HTTP API",
            "handled_by" => "Backend handler",
            "invokes_handler" => "UI event",
            "navigates_to" => "Navigation",
            "uses_azure_service" => "Cloud dependency",
            "executes_procedure" => "Stored procedure",
            "reads_table" => "Database read",
            "writes_table" => "Database write",
            "saves_changes" => "Persistence",
            _ => "Code relationship"
        };

    private static string FormatRelationship(string relationshipKind) =>
        relationshipKind.Replace("_", " ", StringComparison.Ordinal);

    private static IReadOnlyList<RepositoryOverviewItem> BuildStartingPoints(
        IReadOnlyList<RepositoryOverviewItem> apiItems,
        IReadOnlyList<RepositoryOverviewItem> flowItems,
        IReadOnlyList<RepositoryOverviewItem> dataItems,
        IReadOnlyList<RepositoryOverviewItem> azureItems,
        IReadOnlyList<RepositoryOverviewItem> folderItems,
        int take)
    {
        var items = new List<RepositoryOverviewItem>();

        if (flowItems.Count > 0)
        {
            items.Add(new RepositoryOverviewItem(
                "Review detected user/business flows",
                $"{flowItems.Count} cross-stack flows can be opened without knowing routes or table names.",
                "guide",
                flowItems.Sum(x => x.Score)));
        }

        if (apiItems.Count > 0)
        {
            items.Add(new RepositoryOverviewItem(
                "Start from the API surface",
                $"{apiItems.Count} API endpoints show how outside requests enter the backend.",
                "guide",
                apiItems.Sum(x => x.Score)));
        }

        if (dataItems.Count > 0)
        {
            items.Add(new RepositoryOverviewItem(
                "Inspect core data touchpoints",
                $"{dataItems.Count} tables or procedures reveal the persistence layer.",
                "guide",
                dataItems.Sum(x => x.Score)));
        }

        if (azureItems.Count > 0)
        {
            items.Add(new RepositoryOverviewItem(
                "Check Azure dependencies",
                $"{azureItems.Count} Azure service references show external cloud integration.",
                "guide",
                azureItems.Sum(x => x.Score)));
        }

        if (folderItems.Count > 0)
        {
            items.Add(new RepositoryOverviewItem(
                "Walk the main folders",
                $"{folderItems.Count} important folders provide the quickest structural tour.",
                "guide",
                folderItems.Sum(x => x.Score)));
        }

        return items
            .OrderByDescending(x => x.Score)
            .Take(take)
            .ToList();
    }

    private static string BuildOverviewSummary(
        string workspaceKind,
        IReadOnlyList<RepositoryOverviewItem> technologies,
        IReadOnlyList<RepositoryOverviewItem> apis,
        IReadOnlyList<RepositoryOverviewItem> flows,
        IReadOnlyList<RepositoryOverviewItem> dataTouchpoints,
        IReadOnlyList<RepositoryOverviewItem> azureDependencies)
    {
        var technologyText = technologies.Count == 0
            ? "no dominant technology signal yet"
            : string.Join(", ", technologies.Take(4).Select(x => x.Title));

        var capabilities = new List<string>();
        if (apis.Count > 0)
        {
            capabilities.Add($"{apis.Count} API entry points");
        }

        if (flows.Count > 0)
        {
            capabilities.Add($"{flows.Count} cross-stack flows");
        }

        if (dataTouchpoints.Count > 0)
        {
            capabilities.Add($"{dataTouchpoints.Count} data touchpoints");
        }

        if (azureDependencies.Count > 0)
        {
            capabilities.Add($"{azureDependencies.Count} Azure dependencies");
        }

        var capabilityText = capabilities.Count == 0
            ? "relationship details will improve after more analyzers discover connections"
            : string.Join(", ", capabilities);

        return $"This {workspaceKind} workspace appears to use {technologyText}. CodeFlowIQ detected {capabilityText}.";
    }

    private static string GetTopLevelFolder(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var slashIndex = normalized.IndexOf("/", StringComparison.Ordinal);
        return slashIndex <= 0 ? "(root)" : normalized[..slashIndex];
    }

    private static string DescribeFolder(string folder) =>
        folder.Equals("(root)", StringComparison.OrdinalIgnoreCase)
            ? "Files located at the repository root"
            : $"Top-level area containing {folder} files";

    private static string FormatLanguageTitle(string languageId) =>
        languageId switch
        {
            "csharp" => "C# / ASP.NET",
            "sql" => "SQL / T-SQL",
            "typescript" => "TypeScript",
            "javascript" => "JavaScript",
            "html" => "HTML / Angular Templates",
            "json" => "JSON configuration",
            _ => languageId
        };

    private static string NormalizeFlowName(string value)
    {
        var cleaned = ExtractMetadataValue(value, "backendApi")
            ?? ExtractMetadataValue(value, "frontendApi")
            ?? value;

        var methodSeparator = cleaned.IndexOf(' ', StringComparison.Ordinal);
        if (methodSeparator >= 0 && methodSeparator < cleaned.Length - 1)
        {
            cleaned = cleaned[(methodSeparator + 1)..];
        }

        cleaned = cleaned
            .Trim('/')
            .Replace("{", string.Empty, StringComparison.Ordinal)
            .Replace("}", string.Empty, StringComparison.Ordinal)
            .Replace("_", " ", StringComparison.Ordinal)
            .Replace("-", " ", StringComparison.Ordinal)
            .Replace("/", " / ", StringComparison.Ordinal);

        return string.IsNullOrWhiteSpace(cleaned)
            ? "Detected flow"
            : CapitalizeWords(cleaned);
    }

    private static string? ExtractMetadataValue(string metadata, string key)
    {
        foreach (var part in metadata.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = part.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                continue;
            }

            var partKey = part[..separatorIndex];
            if (partKey.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return part[(separatorIndex + 1)..];
            }
        }

        return null;
    }

    private static string FormatIdentifier(string identifier)
    {
        var cleaned = identifier.Split(['\\', '/']).LastOrDefault() ?? identifier;
        var qualifiedIndex = cleaned.LastIndexOf("::", StringComparison.Ordinal);
        if (qualifiedIndex >= 0 && qualifiedIndex < cleaned.Length - 2)
        {
            cleaned = cleaned[(qualifiedIndex + 2)..];
        }

        return cleaned
            .Replace(".cs", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(".ts", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(".sql", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static string CapitalizeWords(string value) =>
        string.Join(' ', value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Length == 0 ? x : $"{char.ToUpperInvariant(x[0])}{x[1..]}"));

    private static string FormatFlowChain(IReadOnlyList<FlowStep> chain, string? format)
    {
        if (chain.Count == 0)
        {
            return string.Empty;
        }

        return NormalizeFlowChainFormat(format) switch
        {
            "tree" => FormatFlowChainTree(chain),
            "json" => FormatFlowChainJson(chain),
            _ => FormatFlowChainCompact(chain)
        };
    }

    private static string FormatFlowChainCompact(IReadOnlyList<FlowStep> chain)
    {
        var parts = new List<string> { FormatNode(chain[0].SourceKind, chain[0].SourceIdentifier) };
        var previousTargetIdentifier = chain[0].SourceIdentifier;
        foreach (var step in chain)
        {
            if (!step.SourceIdentifier.Equals(previousTargetIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                parts.Add("--resolved_to-->");
                parts.Add(FormatNode(step.SourceKind, step.SourceIdentifier));
            }

            parts.Add($"--{step.RelationshipKind}-->");
            parts.Add(FormatNode(step.TargetKind, step.TargetIdentifier));
            previousTargetIdentifier = step.TargetIdentifier;
        }

        return string.Join(' ', parts);
    }

    private static string FormatFlowChainTree(IReadOnlyList<FlowStep> chain)
    {
        var lines = new List<string> { FormatNode(chain[0].SourceKind, chain[0].SourceIdentifier) };
        var previousTargetIdentifier = chain[0].SourceIdentifier;
        var depth = 1;

        foreach (var step in chain)
        {
            if (!step.SourceIdentifier.Equals(previousTargetIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                lines.Add($"{new string(' ', depth * 2)}resolved_to -> {FormatNode(step.SourceKind, step.SourceIdentifier)}");
                depth++;
            }

            lines.Add($"{new string(' ', depth * 2)}{step.RelationshipKind} -> {FormatNode(step.TargetKind, step.TargetIdentifier)}");
            previousTargetIdentifier = step.TargetIdentifier;
            depth++;
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatFlowChainJson(IReadOnlyList<FlowStep> chain)
    {
        var nodes = new List<object>
        {
            new
            {
                kind = chain[0].SourceKind,
                identifier = chain[0].SourceIdentifier
            }
        };
        var edges = new List<object>();
        var previousTargetIdentifier = chain[0].SourceIdentifier;

        foreach (var step in chain)
        {
            if (!step.SourceIdentifier.Equals(previousTargetIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                edges.Add(new
                {
                    relationship = "resolved_to",
                    source = previousTargetIdentifier,
                    target = step.SourceIdentifier
                });
                nodes.Add(new
                {
                    kind = step.SourceKind,
                    identifier = step.SourceIdentifier
                });
            }

            edges.Add(new
            {
                relationship = step.RelationshipKind,
                source = step.SourceIdentifier,
                target = step.TargetIdentifier
            });
            nodes.Add(new
            {
                kind = step.TargetKind,
                identifier = step.TargetIdentifier
            });
            previousTargetIdentifier = step.TargetIdentifier;
        }

        return JsonSerializer.Serialize(new { nodes, edges });
    }

    private static string NormalizeFlowChainFormat(string? format) =>
        format?.Trim().ToLowerInvariant() switch
        {
            "tree" => "tree",
            "json" => "json",
            _ => "compact"
        };

    private static string FormatNode(string kind, string identifier) => $"{kind}:{identifier}";

    private sealed record RuntimeFile(string RelativePath, string LanguageId);

    private sealed record FlowStep(
        string SourceKind,
        string SourceIdentifier,
        string RelationshipKind,
        string TargetKind,
        string TargetIdentifier);

    private sealed class FlowGraph
    {
        private static readonly HashSet<string> TraversableRelationshipKinds = new(StringComparer.Ordinal)
        {
            "calls_method",
            "executes_procedure",
            "reads_table",
            "writes_table",
            "saves_changes"
        };

        private readonly Dictionary<string, List<Core.Models.CodeRelationship>> _outgoing;
        private readonly Dictionary<string, List<string>> _classMethods;
        private readonly Dictionary<string, string> _serviceImplementations;
        private readonly Dictionary<string, List<string>> _methodsByName;

        private FlowGraph(
            Dictionary<string, List<Core.Models.CodeRelationship>> outgoing,
            Dictionary<string, List<string>> classMethods,
            Dictionary<string, string> serviceImplementations,
            Dictionary<string, List<string>> methodsByName)
        {
            _outgoing = outgoing;
            _classMethods = classMethods;
            _serviceImplementations = serviceImplementations;
            _methodsByName = methodsByName;
        }

        public static FlowGraph Create(IReadOnlyList<Core.Models.CodeRelationship> relationships)
        {
            var outgoing = relationships
                .Where(x => TraversableRelationshipKinds.Contains(x.RelationshipKind))
                .GroupBy(x => x.SourceIdentifier, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);

            var qualifiedClassMethods = relationships
                .Where(x => x.RelationshipKind == "contains_symbol" && x.SourceKind == "symbol" && x.TargetKind == "method")
                .GroupBy(x => x.SourceIdentifier, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Select(y => y.TargetIdentifier).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
            var classMethods = new Dictionary<string, List<string>>(qualifiedClassMethods, StringComparer.OrdinalIgnoreCase);
            foreach (var item in qualifiedClassMethods)
            {
                var className = GetMemberName(item.Key);
                if (!classMethods.TryGetValue(className, out var methods))
                {
                    classMethods[className] = [.. item.Value];
                    continue;
                }

                methods.AddRange(item.Value.Where(x => !methods.Contains(x, StringComparer.OrdinalIgnoreCase)));
            }

            var serviceImplementations = relationships
                .Where(x => x.RelationshipKind == "implemented_by")
                .GroupBy(x => x.SourceIdentifier, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First().TargetIdentifier, StringComparer.OrdinalIgnoreCase);

            var methodsByName = relationships
                .Where(x => x.RelationshipKind == "contains_symbol" && x.TargetKind == "method")
                .GroupBy(x => GetMemberName(x.TargetIdentifier), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Select(y => y.TargetIdentifier).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);

            return new FlowGraph(outgoing, classMethods, serviceImplementations, methodsByName);
        }

        public IEnumerable<IReadOnlyList<FlowStep>> Expand(FlowStep seed, int maxDepth)
        {
            var initialChain = new List<FlowStep> { seed };
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { seed.SourceIdentifier, seed.TargetIdentifier };
            var expanded = false;

            foreach (var chain in ExpandFrom(seed.TargetIdentifier, initialChain, visited, Math.Max(1, maxDepth)))
            {
                expanded = true;
                yield return chain;
            }

            if (!expanded)
            {
                yield return initialChain;
            }
        }

        private IEnumerable<IReadOnlyList<FlowStep>> ExpandFrom(
            string sourceIdentifier,
            List<FlowStep> chain,
            HashSet<string> visited,
            int remainingDepth)
        {
            if (remainingDepth <= 0)
            {
                yield return chain.ToList();
                yield break;
            }

            var nextSteps = GetNextSteps(sourceIdentifier).ToList();
            if (nextSteps.Count == 0)
            {
                yield return chain.ToList();
                yield break;
            }

            foreach (var step in nextSteps)
            {
                if (!visited.Add(step.TargetIdentifier))
                {
                    continue;
                }

                chain.Add(step);
                foreach (var expanded in ExpandFrom(step.TargetIdentifier, chain, visited, remainingDepth - 1))
                {
                    yield return expanded;
                }

                chain.RemoveAt(chain.Count - 1);
                visited.Remove(step.TargetIdentifier);
            }
        }

        private IEnumerable<FlowStep> GetNextSteps(string sourceIdentifier)
        {
            if (_outgoing.TryGetValue(sourceIdentifier, out var directRelationships))
            {
                foreach (var relationship in directRelationships)
                {
                    yield return ToStep(relationship);
                }
            }

            foreach (var resolvedSource in ResolveCallableSource(sourceIdentifier))
            {
                if (!_outgoing.TryGetValue(resolvedSource, out var resolvedRelationships))
                {
                    continue;
                }

                foreach (var relationship in resolvedRelationships)
                {
                    yield return ToStep(relationship);
                }
            }
        }

        private IEnumerable<string> ResolveCallableSource(string identifier)
        {
            var memberName = GetCallableMemberName(identifier);
            if (memberName is null)
            {
                yield break;
            }

            var serviceName = GetCallableReceiverName(identifier);
            if (serviceName is not null && _serviceImplementations.TryGetValue(serviceName, out var implementationClass))
            {
                if (_classMethods.TryGetValue(implementationClass, out var methods))
                {
                    var resolvedMethods = methods
                        .Where(x => GetMemberName(x).Equals(memberName, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    foreach (var method in resolvedMethods)
                    {
                        yield return method;
                    }

                    if (resolvedMethods.Count > 0)
                    {
                        yield break;
                    }
                }
            }

            if (serviceName is not null)
            {
                var preferredClassName = serviceName.StartsWith("I", StringComparison.Ordinal) && serviceName.Length > 1
                    ? serviceName[1..]
                    : serviceName;
                var preferredMethods = _classMethods
                    .Where(x => GetMemberName(x.Key).Equals(preferredClassName, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(x => x.Value)
                    .Where(x => GetMemberName(x).Equals(memberName, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var method in preferredMethods)
                {
                    yield return method;
                }

                if (preferredMethods.Count > 0)
                {
                    yield break;
                }
            }

            if (_methodsByName.TryGetValue(memberName, out var matchingMethods))
            {
                foreach (var method in matchingMethods)
                {
                    yield return method;
                }
            }
        }

        private static FlowStep ToStep(Core.Models.CodeRelationship relationship) =>
            new(
                relationship.SourceKind,
                relationship.SourceIdentifier,
                relationship.RelationshipKind,
                relationship.TargetKind,
                relationship.TargetIdentifier);

        private static string? GetCallableReceiverName(string identifier)
        {
            var separatorIndex = identifier.LastIndexOf(".", StringComparison.Ordinal);
            return separatorIndex <= 0 ? null : identifier[..separatorIndex];
        }

        private static string? GetCallableMemberName(string identifier)
        {
            var separatorIndex = identifier.LastIndexOf(".", StringComparison.Ordinal);
            if (separatorIndex > 0 && separatorIndex < identifier.Length - 1)
            {
                return identifier[(separatorIndex + 1)..];
            }

            var memberName = GetMemberName(identifier);
            return string.IsNullOrWhiteSpace(memberName) ? null : memberName;
        }

        private static string GetMemberName(string identifier)
        {
            var qualifiedIndex = identifier.LastIndexOf("::", StringComparison.Ordinal);
            return qualifiedIndex >= 0 ? identifier[(qualifiedIndex + 2)..] : identifier;
        }
    }
}
