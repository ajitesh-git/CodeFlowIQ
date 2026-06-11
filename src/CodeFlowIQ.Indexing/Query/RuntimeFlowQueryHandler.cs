using CodeFlowIQ.Core.Query;
using CodeFlowIQ.Data;
using Microsoft.EntityFrameworkCore;

namespace CodeFlowIQ.Indexing;

public sealed class RuntimeFlowQueryHandler
{
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
            fileQuery = WorkspaceQueryFilters.ExcludeTestFiles(fileQuery);
            relationshipQuery = WorkspaceQueryFilters.ExcludeTestRelationships(relationshipQuery);
        }

        var files = await fileQuery
            .Select(x => new RuntimeFile(x.Id, x.RelativePath, x.LanguageId))
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
                    WorkspaceQueryText.FormatIdentifier(path),
                    path,
                    "frontend-startup",
                    fileName.Contains("routing", StringComparison.OrdinalIgnoreCase) ? 90 : 80,
                    FileExplorerItemId(file.Id)));
            }

            if (fileName.Equals("Program.cs", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("Startup.cs", StringComparison.OrdinalIgnoreCase))
            {
                entries.Add(new RuntimeEntryPoint(
                    WorkspaceQueryText.FormatIdentifier(path),
                    path,
                    "backend-startup",
                    90,
                    FileExplorerItemId(file.Id)));
            }

            if (fileName.Equals("host.json", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("function.json", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("local.settings.json", StringComparison.OrdinalIgnoreCase))
            {
                entries.Add(new RuntimeEntryPoint(
                    WorkspaceQueryText.FormatIdentifier(path),
                    path,
                    "function-worker",
                    85,
                    FileExplorerItemId(file.Id)));
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
            var api = WorkspaceQueryText.ExtractMetadataValue(relationship.Metadata ?? string.Empty, "backendApi")
                ?? WorkspaceQueryText.ExtractMetadataValue(relationship.Metadata ?? string.Empty, "frontendApi")
                ?? "Matched API call";
            var steps = new List<RuntimeFlowStep>();
            steps.AddRange(BuildUpstreamRuntimeSteps(relationships, relationship.SourceIdentifier, 4));
            steps.AddRange(new RuntimeFlowStep[]
            {
                new(
                    "Frontend",
                    WorkspaceQueryText.FormatIdentifier(relationship.SourceIdentifier),
                    relationship.SourceIdentifier,
                    relationship.SourceKind,
                    "source",
                    "files",
                    BuildExplorerQuery(relationship.SourceIdentifier)),
                new(
                    "HTTP API",
                    api,
                    "Client request crosses into backend API surface.",
                    "api",
                    "relationship",
                    "backend",
                    BuildExplorerQuery(relationship.TargetIdentifier),
                    RelationshipExplorerItemId(relationship.Id)),
                new(
                    "Backend",
                    WorkspaceQueryText.FormatIdentifier(relationship.TargetIdentifier),
                    relationship.TargetIdentifier,
                    relationship.TargetKind,
                    "relationship",
                    "backend",
                    BuildExplorerQuery(relationship.TargetIdentifier),
                    RelationshipExplorerItemId(relationship.Id))
            });
            steps.AddRange(BuildDownstreamRuntimeSteps(relationships, relationship.TargetIdentifier, 4));
            steps.AddRange(BuildRelatedNavigationSteps(relationships, steps, relationship.SourceIdentifier));

            flows.Add(new RuntimeFlow(
                WorkspaceQueryText.NormalizeFlowName(api),
                $"{WorkspaceQueryText.FormatIdentifier(relationship.SourceIdentifier)} reaches {WorkspaceQueryText.FormatIdentifier(relationship.TargetIdentifier)}.",
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
                new(
                    "HTTP API",
                    relationship.TargetIdentifier,
                    "Outside request enters the backend here.",
                    "api",
                    "relationship",
                    "backend",
                    BuildExplorerQuery(relationship.SourceIdentifier),
                    RelationshipExplorerItemId(relationship.Id)),
                new(
                    "Backend",
                    WorkspaceQueryText.FormatIdentifier(relationship.SourceIdentifier),
                    relationship.SourceIdentifier,
                    relationship.SourceKind,
                    "relationship",
                    "backend",
                    BuildExplorerQuery(relationship.SourceIdentifier),
                    RelationshipExplorerItemId(relationship.Id))
            };
            steps.AddRange(BuildDownstreamRuntimeSteps(relationships, relationship.SourceIdentifier, 4));

            flows.Add(new RuntimeFlow(
                WorkspaceQueryText.NormalizeFlowName(relationship.TargetIdentifier),
                $"{relationship.TargetIdentifier} is handled by {WorkspaceQueryText.FormatIdentifier(relationship.SourceIdentifier)}.",
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
                new(
                    "UI event",
                    WorkspaceQueryText.FormatIdentifier(relationship.SourceIdentifier),
                    relationship.SourceIdentifier,
                    relationship.SourceKind,
                    "source",
                    "files",
                    BuildExplorerQuery(relationship.SourceIdentifier)),
                new(
                    "Frontend handler",
                    WorkspaceQueryText.FormatIdentifier(relationship.TargetIdentifier),
                    relationship.TargetIdentifier,
                    relationship.TargetKind,
                    "relationship",
                    "backend",
                    BuildExplorerQuery(relationship.TargetIdentifier),
                    RelationshipExplorerItemId(relationship.Id))
            };
            steps.AddRange(BuildDownstreamRuntimeSteps(relationships, relationship.TargetIdentifier, 4));
            steps.AddRange(BuildRelatedNavigationSteps(relationships, steps, relationship.TargetIdentifier));

            flows.Add(new RuntimeFlow(
                WorkspaceQueryText.FormatIdentifier(relationship.TargetIdentifier),
                $"{WorkspaceQueryText.FormatIdentifier(relationship.SourceIdentifier)} invokes {WorkspaceQueryText.FormatIdentifier(relationship.TargetIdentifier)}.",
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
                $"{relationship.SourceIdentifier} renders {WorkspaceQueryText.FormatIdentifier(relationship.TargetIdentifier)}.",
                "frontend-route",
                62,
                [
                    new(
                        "Route",
                        relationship.SourceIdentifier,
                        "Client-side route selected by the frontend router.",
                        relationship.SourceKind,
                        "source",
                        "files",
                        BuildExplorerQuery(relationship.SourceIdentifier)),
                    new(
                        "Page/component",
                        WorkspaceQueryText.FormatIdentifier(relationship.TargetIdentifier),
                        relationship.TargetIdentifier,
                        relationship.TargetKind,
                        "source",
                        "files",
                        BuildExplorerQuery(relationship.TargetIdentifier))
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
                        new(
                            "Entry point",
                            entryPoint.Title,
                            entryPoint.Detail,
                            entryPoint.Category,
                            "source",
                            "files",
                            BuildExplorerQuery(entryPoint.Detail),
                            entryPoint.RepositoryExplorerItemId)
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
                new(
                    "Entry point",
                    entryPoint.Title,
                    entryPoint.Detail,
                    entryPoint.Category,
                    "source",
                    "files",
                    BuildExplorerQuery(entryPoint.Detail),
                    entryPoint.RepositoryExplorerItemId),
                new(
                    "Backend",
                    WorkspaceQueryText.FormatIdentifier(seed.SourceIdentifier),
                    seed.SourceIdentifier,
                    seed.SourceKind,
                    "relationship",
                    "backend",
                    BuildExplorerQuery(seed.SourceIdentifier),
                    RelationshipExplorerItemId(seed.Id)),
                new(
                    StageForRelationship(seed.RelationshipKind),
                    WorkspaceQueryText.FormatIdentifier(seed.TargetIdentifier),
                    $"{FormatRelationship(seed.RelationshipKind)} from {WorkspaceQueryText.FormatIdentifier(seed.SourceIdentifier)}.",
                    seed.TargetKind,
                    "relationship",
                    ExplorerSurfaceForRelationship(seed.RelationshipKind),
                    BuildExplorerQuery(seed.TargetIdentifier),
                    RelationshipExplorerItemId(seed.Id))
            };
            steps.AddRange(BuildDownstreamRuntimeSteps(relationships, seed.TargetIdentifier, 4));

            flows.Add(new RuntimeFlow(
                $"{WorkspaceQueryText.FormatIdentifier(seed.SourceIdentifier)} to {WorkspaceQueryText.FormatIdentifier(seed.TargetIdentifier)}",
                $"{entryPoint.Title} is associated with {WorkspaceQueryText.FormatIdentifier(seed.SourceIdentifier)}, which {FormatRelationship(seed.RelationshipKind).ToLowerInvariant()} {WorkspaceQueryText.FormatIdentifier(seed.TargetIdentifier)}.",
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
            "Company",
            "Platform",
            "Product",
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
                WorkspaceQueryText.FormatIdentifier(next.TargetIdentifier),
                $"{FormatRelationship(next.RelationshipKind)} from {WorkspaceQueryText.FormatIdentifier(next.SourceIdentifier)}.",
                next.TargetKind,
                "relationship",
                ExplorerSurfaceForRelationship(next.RelationshipKind),
                BuildExplorerQuery(next.TargetIdentifier),
                RelationshipExplorerItemId(next.Id)));
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
                $"{WorkspaceQueryText.FormatIdentifier(x.SourceIdentifier)} navigates to this client route.",
                x.TargetKind,
                "relationship",
                "files",
                BuildExplorerQuery(x.TargetIdentifier)))
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
                WorkspaceQueryText.FormatIdentifier(previous.SourceIdentifier),
                $"{FormatRelationship(previous.RelationshipKind)} into {WorkspaceQueryText.FormatIdentifier(previous.TargetIdentifier)}.",
                previous.SourceKind,
                "relationship",
                ExplorerSurfaceForRelationship(previous.RelationshipKind),
                BuildExplorerQuery(previous.SourceIdentifier),
                RelationshipExplorerItemId(previous.Id)));
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
                $"Route renders {WorkspaceQueryText.FormatIdentifier(route.TargetIdentifier)}.",
                route.SourceKind,
                "relationship",
                "files",
                BuildExplorerQuery(route.SourceIdentifier)));
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

    private static string ExplorerSurfaceForRelationship(string relationshipKind) =>
        relationshipKind switch
        {
            "uses_azure_service" => "azure",
            "navigates_to" or "renders_component" => "files",
            _ => "backend"
        };

    private static string BuildExplorerQuery(string identifier)
    {
        var cleaned = identifier.Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return string.Empty;
        }

        var qualifiedIndex = cleaned.LastIndexOf("::", StringComparison.Ordinal);
        if (qualifiedIndex >= 0 && qualifiedIndex < cleaned.Length - 2)
        {
            cleaned = cleaned[(qualifiedIndex + 2)..];
        }

        var pathSegment = cleaned
            .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();

        cleaned = pathSegment ?? cleaned;
        return cleaned
            .Replace(".cs", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(".ts", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(".tsx", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(".jsx", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(".sql", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(".json", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static string FileExplorerItemId(int id) => $"file:{id}";

    private static string RelationshipExplorerItemId(int id) => $"relationship:{id}";

    private sealed record RuntimeFile(int Id, string RelativePath, string LanguageId);
}

