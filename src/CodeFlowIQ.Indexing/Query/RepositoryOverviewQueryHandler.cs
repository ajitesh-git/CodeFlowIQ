using CodeFlowIQ.Core.Query;
using CodeFlowIQ.Data;
using Microsoft.EntityFrameworkCore;

namespace CodeFlowIQ.Indexing;

public sealed class RepositoryOverviewQueryHandler
{
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
            files = WorkspaceQueryFilters.ExcludeTestFiles(files);
            relationships = WorkspaceQueryFilters.ExcludeTestRelationships(relationships);
        }

        var languageCounts = await files
            .GroupBy(x => x.LanguageId)
            .OrderByDescending(x => x.Count())
            .Take(normalizedTake)
            .Select(x => new RepositoryOverviewItem(
                WorkspaceQueryText.FormatLanguageTitle(x.Key),
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
            .GroupBy(x => WorkspaceQueryText.NormalizeFlowName(x.Metadata ?? x.TargetIdentifier), StringComparer.OrdinalIgnoreCase)
            .Select(x =>
            {
                var first = x.First();
                return new RepositoryOverviewItem(
                    x.Key,
                    $"{WorkspaceQueryText.FormatIdentifier(first.SourceIdentifier)} connects to {WorkspaceQueryText.FormatIdentifier(first.TargetIdentifier)}",
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
                WorkspaceQueryText.FormatIdentifier(x.Key.TargetIdentifier),
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
                WorkspaceQueryText.FormatIdentifier(x.Key),
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

}
