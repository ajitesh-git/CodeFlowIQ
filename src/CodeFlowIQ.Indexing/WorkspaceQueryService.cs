using CodeFlowIQ.Core.Query;
using CodeFlowIQ.Data;
using Microsoft.EntityFrameworkCore;

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

    public async Task<IReadOnlyList<string>> ListFilesAsync(string workspacePath, string? languageId, int take, CancellationToken cancellationToken)
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
}
