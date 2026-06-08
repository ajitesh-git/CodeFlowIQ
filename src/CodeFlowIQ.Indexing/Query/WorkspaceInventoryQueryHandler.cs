using CodeFlowIQ.Core.Query;
using CodeFlowIQ.Data;
using Microsoft.EntityFrameworkCore;

namespace CodeFlowIQ.Indexing;

public sealed class WorkspaceInventoryQueryHandler
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
        query = WorkspaceQueryFilters.ApplyRelationshipFilters(query, searchText, relationshipKind, sourceText, targetText, includeTests);

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
            query = WorkspaceQueryFilters.ExcludeTestRelationships(query);
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
            query = WorkspaceQueryFilters.ExcludeTestRelationships(query);
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

    public async Task<IReadOnlyList<RepositoryExplorerItem>> ListRepositoryExplorerItemsAsync(
        string workspacePath,
        string surface,
        string? queryText,
        string? selectedItemId,
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

        var normalizedSurface = NormalizeExplorerSurface(surface);
        var rows = normalizedSurface switch
        {
            "files" => await ListFileExplorerItemsAsync(db, workspace.Id, queryText, includeTests, take, cancellationToken),
            "apis" => await ListRelationshipExplorerItemsAsync(
                db,
                workspace.Id,
                "apis",
                ["handles_api"],
                queryText,
                includeTests,
                take,
                cancellationToken),
            "azure" => await ListRelationshipExplorerItemsAsync(
                db,
                workspace.Id,
                "azure",
                ["uses_azure_service"],
                queryText,
                includeTests,
                take,
                cancellationToken),
            _ => await ListRelationshipExplorerItemsAsync(
                db,
                workspace.Id,
                "backend",
                ["calls_method", "executes_procedure", "reads_table", "writes_table", "saves_changes", "handled_by", "calls_api", "invokes_handler", "matches_backend_handler"],
                queryText,
                includeTests,
                take,
                cancellationToken)
        };

        if (!string.IsNullOrWhiteSpace(selectedItemId)
            && !rows.Any(x => x.Id.Equals(selectedItemId, StringComparison.OrdinalIgnoreCase)))
        {
            var selectedItem = await GetSelectedExplorerItemAsync(db, workspace.Id, normalizedSurface, selectedItemId, cancellationToken);
            if (selectedItem is not null)
            {
                return new[] { selectedItem }.Concat(rows).ToList();
            }
        }

        return rows;
    }

    private static async Task<IReadOnlyList<RepositoryExplorerItem>> ListFileExplorerItemsAsync(
        CodeFlowIqDbContext db,
        int workspaceId,
        string? queryText,
        bool includeTests,
        int take,
        CancellationToken cancellationToken)
    {
        var query = db.IndexedFiles.Where(x => x.WorkspaceId == workspaceId && !x.IsDeleted);
        if (!includeTests)
        {
            query = WorkspaceQueryFilters.ExcludeTestFiles(query);
        }

        if (!string.IsNullOrWhiteSpace(queryText))
        {
            query = query.Where(x =>
                EF.Functions.Like(x.RelativePath, $"%{queryText}%")
                || EF.Functions.Like(x.LanguageId, $"%{queryText}%")
                || (x.ParseStatus != null && EF.Functions.Like(x.ParseStatus, $"%{queryText}%")));
        }

        return await query
            .OrderBy(x => x.RelativePath)
            .Take(take)
            .Select(x => new RepositoryExplorerItem(
                $"file:{x.Id}",
                "files",
                x.RelativePath,
                x.LanguageId,
                $"{x.LanguageId}\t{x.RelativePath}",
                "file",
                x.RelativePath,
                null,
                null,
                null,
                x.RelativePath,
                null,
                x.ParseStatus))
            .ToListAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<RepositoryExplorerItem>> ListRelationshipExplorerItemsAsync(
        CodeFlowIqDbContext db,
        int workspaceId,
        string surface,
        IReadOnlyList<string> relationshipKinds,
        string? queryText,
        bool includeTests,
        int take,
        CancellationToken cancellationToken)
    {
        var kinds = relationshipKinds.ToHashSet(StringComparer.Ordinal);
        var query = db.CodeRelationships.Where(x => x.WorkspaceId == workspaceId && kinds.Contains(x.RelationshipKind));
        if (!includeTests)
        {
            query = WorkspaceQueryFilters.ExcludeTestRelationships(query);
        }

        if (!string.IsNullOrWhiteSpace(queryText))
        {
            query = query.Where(x =>
                EF.Functions.Like(x.SourceIdentifier, $"%{queryText}%")
                || EF.Functions.Like(x.TargetIdentifier, $"%{queryText}%")
                || EF.Functions.Like(x.RelationshipKind, $"%{queryText}%")
                || (x.Metadata != null && EF.Functions.Like(x.Metadata, $"%{queryText}%")));
        }

        var relationships = await query
            .OrderBy(x => x.RelationshipKind)
            .ThenBy(x => x.SourceIdentifier)
            .ThenBy(x => x.TargetIdentifier)
            .Take(take)
            .ToListAsync(cancellationToken);

        return relationships
            .Select(x => new RepositoryExplorerItem(
                $"relationship:{x.Id}",
                surface,
                FormatExplorerTitle(surface, x.SourceIdentifier, x.TargetIdentifier, x.TargetKind),
                FormatExplorerSubtitle(x.SourceKind, x.RelationshipKind, x.TargetKind),
                $"{x.SourceKind}:{x.SourceIdentifier}\t{x.RelationshipKind}\t{x.TargetKind}:{x.TargetIdentifier}",
                x.SourceKind,
                x.SourceIdentifier,
                x.RelationshipKind,
                x.TargetKind,
                x.TargetIdentifier,
                GetSourceFilePath(x.SourceIdentifier),
                GetMetadataLineNumber(x.Metadata),
                x.Metadata))
            .ToList();
    }

    private static async Task<RepositoryExplorerItem?> GetSelectedExplorerItemAsync(
        CodeFlowIqDbContext db,
        int workspaceId,
        string surface,
        string selectedItemId,
        CancellationToken cancellationToken)
    {
        if (surface == "files"
            && selectedItemId.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(selectedItemId["file:".Length..], out var fileId))
        {
            return await db.IndexedFiles
                .Where(x => x.WorkspaceId == workspaceId && x.Id == fileId && !x.IsDeleted)
                .Select(x => new RepositoryExplorerItem(
                    $"file:{x.Id}",
                    "files",
                    x.RelativePath,
                    x.LanguageId,
                    $"{x.LanguageId}\t{x.RelativePath}",
                    "file",
                    x.RelativePath,
                    null,
                    null,
                    null,
                    x.RelativePath,
                    null,
                    x.ParseStatus))
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (!selectedItemId.StartsWith("relationship:", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(selectedItemId["relationship:".Length..], out var relationshipId))
        {
            return null;
        }

        var relationship = await db.CodeRelationships
            .Where(x => x.WorkspaceId == workspaceId && x.Id == relationshipId)
            .FirstOrDefaultAsync(cancellationToken);
        if (relationship is null)
        {
            return null;
        }

        return new RepositoryExplorerItem(
            $"relationship:{relationship.Id}",
            surface,
            FormatExplorerTitle(surface, relationship.SourceIdentifier, relationship.TargetIdentifier, relationship.TargetKind),
            FormatExplorerSubtitle(relationship.SourceKind, relationship.RelationshipKind, relationship.TargetKind),
            $"{relationship.SourceKind}:{relationship.SourceIdentifier}\t{relationship.RelationshipKind}\t{relationship.TargetKind}:{relationship.TargetIdentifier}",
            relationship.SourceKind,
            relationship.SourceIdentifier,
            relationship.RelationshipKind,
            relationship.TargetKind,
            relationship.TargetIdentifier,
            GetSourceFilePath(relationship.SourceIdentifier),
            GetMetadataLineNumber(relationship.Metadata),
            relationship.Metadata);
    }

    private static string NormalizeExplorerSurface(string surface) =>
        surface.Trim().ToLowerInvariant() switch
        {
            "files" => "files",
            "apis" => "apis",
            "azure" => "azure",
            _ => "backend"
        };

    private static string FormatExplorerTitle(string surface, string sourceIdentifier, string targetIdentifier, string? targetKind)
    {
        var target = FormatIdentifier(targetIdentifier);
        if (string.IsNullOrWhiteSpace(target))
        {
            target = $"Unresolved {targetKind ?? "target"}";
        }

        return surface == "apis" || surface == "azure"
            ? target
            : $"{FormatIdentifier(sourceIdentifier)} -> {target}";
    }

    private static string FormatExplorerSubtitle(string sourceKind, string relationshipKind, string? targetKind) =>
        $"{sourceKind} / {relationshipKind} / {targetKind}";

    private static string FormatIdentifier(string identifier)
    {
        var cleaned = (identifier.Split(['\\', '/']).LastOrDefault() ?? identifier).Trim();
        var qualifiedIndex = cleaned.LastIndexOf("::", StringComparison.Ordinal);
        return qualifiedIndex >= 0 && qualifiedIndex < cleaned.Length - 2
            ? cleaned[(qualifiedIndex + 2)..]
            : cleaned;
    }

    private static string? GetSourceFilePath(string sourceIdentifier)
    {
        var separatorIndex = sourceIdentifier.LastIndexOf("::", StringComparison.Ordinal);
        var filePath = separatorIndex >= 0 ? sourceIdentifier[..separatorIndex] : sourceIdentifier;
        return filePath.Contains('\\') || filePath.Contains('/') ? filePath : null;
    }

    private static int? GetMetadataLineNumber(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return null;
        }

        const string linePrefix = "line=";
        var lineIndex = metadata.IndexOf(linePrefix, StringComparison.OrdinalIgnoreCase);
        if (lineIndex < 0)
        {
            return null;
        }

        var start = lineIndex + linePrefix.Length;
        var end = start;
        while (end < metadata.Length && char.IsDigit(metadata[end]))
        {
            end++;
        }

        return int.TryParse(metadata[start..end], out var lineNumber) ? lineNumber : null;
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
            relationships = WorkspaceQueryFilters.ExcludeTestRelationships(relationships);
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
}

