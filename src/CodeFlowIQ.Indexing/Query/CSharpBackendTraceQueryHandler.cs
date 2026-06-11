using CodeFlowIQ.Core.Models;
using CodeFlowIQ.Core.Query;
using CodeFlowIQ.Data;
using Microsoft.EntityFrameworkCore;

namespace CodeFlowIQ.Indexing;

public sealed class CSharpBackendTraceQueryHandler
{
    public async Task<IReadOnlyList<string>> ListEntriesAsync(
        string workspacePath,
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

        var relationshipQuery = db.CodeRelationships.Where(x => x.WorkspaceId == workspace.Id);
        if (!includeTests)
        {
            relationshipQuery = WorkspaceQueryFilters.ExcludeTestRelationships(relationshipQuery);
        }

        var relationships = await relationshipQuery
            .Where(x => (x.RelationshipKind == "handled_by" && x.SourceKind == "api")
                || (x.RelationshipKind == "contains_symbol" && x.TargetKind == "method"))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var apiEntries = relationships
            .Where(x => x.RelationshipKind == "handled_by" && x.SourceKind == "api")
            .Select(x => x.SourceIdentifier);

        var methodEntries = relationships
            .Where(x => x.RelationshipKind == "contains_symbol" && x.TargetKind == "method")
            .Select(x => x.TargetIdentifier);

        return apiEntries
            .Concat(methodEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToList();
    }

    public async Task<CSharpBackendTrace?> GetTraceAsync(
        string workspacePath,
        string entry,
        bool includeTests,
        int maxDepth,
        CancellationToken cancellationToken)
    {
        var rootPath = Path.GetFullPath(workspacePath);
        await using var db = await WorkspaceDatabase.OpenMigratedAsync(rootPath, cancellationToken);

        var workspace = await db.Workspaces.FirstOrDefaultAsync(x => x.RootPath == rootPath, cancellationToken);
        if (workspace is null)
        {
            return null;
        }

        var relationshipQuery = db.CodeRelationships.Where(x => x.WorkspaceId == workspace.Id);
        if (!includeTests)
        {
            relationshipQuery = WorkspaceQueryFilters.ExcludeTestRelationships(relationshipQuery);
        }

        var relationships = await relationshipQuery
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var graph = CSharpTraceGraph.Create(relationships);
        var start = graph.FindEntry(entry);
        if (start is null)
        {
            return new CSharpBackendTrace(entry, "not-found", [], [$"No C# backend entry point matched '{entry}'."]);
        }

        var steps = new List<CSharpBackendTraceStep>();
        var warnings = new List<string>();
        var currentMethod = start.TargetMethodIdentifier;

        if (start.Relationship is not null)
        {
            AddStep(
                steps,
                "API route",
                start.Relationship.SourceIdentifier,
                $"Handled by {FormatIdentifier(start.Relationship.TargetIdentifier)}.",
                "confirmed",
                "Found ASP.NET API route evidence.",
                start.Relationship);
        }
        else
        {
            AddSyntheticStep(
                steps,
                "Method start",
                $"Start at {FormatIdentifier(currentMethod)}",
                "Tracing starts from the selected indexed C# method.",
                "confirmed",
                "Found an indexed C# method matching the selected entry.",
                "method",
                currentMethod,
                null,
                null,
                null);
        }

        graph.TraceMethod(
            currentMethod,
            runtimeClass: null,
            maxSteps: Math.Clamp(maxDepth, 1, 200),
            steps,
            warnings,
            visiting: [],
            cancellationToken);

        EnrichTraceSteps(rootPath, relationships, steps);
        var hiddenStepCount = steps.Count(x => x.IsHiddenByDefault);
        var hasMore = warnings.Any(x => x.StartsWith("Stopped after ", StringComparison.OrdinalIgnoreCase));
        var continuationEntry = steps.LastOrDefault(x => !string.IsNullOrWhiteSpace(x.ContinuationEntry))?.ContinuationEntry;
        var stopReason = hasMore
            ? warnings.LastOrDefault(x => x.StartsWith("Stopped after ", StringComparison.OrdinalIgnoreCase))
            : warnings.LastOrDefault();

        return new CSharpBackendTrace(entry, "ok", steps, warnings, hiddenStepCount, hasMore, continuationEntry, stopReason);
    }

    private static void AddStep(
        List<CSharpBackendTraceStep> steps,
        string stage,
        string title,
        string detail,
        string confidence,
        string reason,
        CodeRelationship relationship)
    {
        steps.Add(new CSharpBackendTraceStep(
            steps.Count + 1,
            stage,
            title,
            detail,
            confidence,
            reason,
            $"relationship:{relationship.Id}",
            relationship.SourceKind,
            relationship.SourceIdentifier,
            relationship.TargetKind,
            relationship.TargetIdentifier,
            relationship.Metadata));
    }

    private static void AddSyntheticStep(
        List<CSharpBackendTraceStep> steps,
        string stage,
        string title,
        string detail,
        string confidence,
        string reason,
        string? sourceKind,
        string? sourceIdentifier,
        string? targetKind,
        string? targetIdentifier,
        string? metadata)
    {
        steps.Add(new CSharpBackendTraceStep(
            steps.Count + 1,
            stage,
            title,
            detail,
            confidence,
            reason,
            null,
            sourceKind,
            sourceIdentifier,
            targetKind,
            targetIdentifier,
            metadata));
    }

    private static string StageForRelationship(string relationshipKind) =>
        relationshipKind switch
        {
            "executes_procedure" => "SQL procedure",
            "reads_table" => "SQL read",
            "writes_table" => "SQL write",
            "saves_changes" => "Database save",
            _ => "Backend call"
        };

    private static string FormatRelationshipTitle(CodeRelationship relationship) =>
        relationship.RelationshipKind switch
        {
            "executes_procedure" => $"Executes {FormatIdentifier(relationship.TargetIdentifier)}",
            "reads_table" => $"Reads {FormatIdentifier(relationship.TargetIdentifier)}",
            "writes_table" => $"Writes {FormatIdentifier(relationship.TargetIdentifier)}",
            "saves_changes" => $"Saves changes through {FormatIdentifier(relationship.TargetIdentifier)}",
            _ => $"Calls {FormatIdentifier(relationship.TargetIdentifier)}"
        };

    private static string FormatRelationshipDetail(CodeRelationship relationship) =>
        $"{FormatIdentifier(relationship.SourceIdentifier)} -> {relationship.RelationshipKind.Replace('_', ' ')} -> {FormatIdentifier(relationship.TargetIdentifier)}";

    private static string FormatIdentifier(string identifier)
    {
        var value = identifier;
        var markerIndex = value.LastIndexOf("::", StringComparison.Ordinal);
        if (markerIndex >= 0)
        {
            value = value[(markerIndex + 2)..];
        }

        return value;
    }

    private static void EnrichTraceSteps(
        string rootPath,
        IReadOnlyList<CodeRelationship> relationships,
        List<CSharpBackendTraceStep> steps)
    {
        var symbolLines = relationships
            .Where(x => x.RelationshipKind == "contains_symbol" && !string.IsNullOrWhiteSpace(x.Metadata))
            .GroupBy(x => x.TargetIdentifier, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => MetadataIntValue(x.First().Metadata, "line"),
                StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var category = CategoryForStage(step.Stage);
            var isFrameworkCall = IsFrameworkOrExternalStep(step);
            var isBoundary = step.Stage.Equals("DI boundary", StringComparison.OrdinalIgnoreCase)
                || step.Stage.Equals("Unresolved call", StringComparison.OrdinalIgnoreCase);
            var hiddenByDefault = isFrameworkCall;
            var hiddenReason = hiddenByDefault ? "Framework or package call hidden by default." : null;
            var sourceIdentifier = PreferredSourceIdentifier(step);
            var sourceFilePath = ExtractRelativePath(sourceIdentifier);
            var lineNumber = ResolveLineNumber(sourceIdentifier, step.Metadata, symbolLines);
            var preview = ReadSourcePreview(rootPath, sourceFilePath, lineNumber);

            steps[index] = step with
            {
                Category = category,
                IsFrameworkCall = isFrameworkCall,
                IsBoundary = isBoundary,
                IsHiddenByDefault = hiddenByDefault,
                HiddenReason = hiddenReason,
                SourceFilePath = sourceFilePath,
                SourceLineNumber = lineNumber,
                SourcePreview = preview,
                ContinuationEntry = IsTraceableMethod(step.TargetIdentifier) ? step.TargetIdentifier : null
            };
        }
    }

    private static string CategoryForStage(string stage)
    {
        if (stage.Contains("SQL", StringComparison.OrdinalIgnoreCase)
            || stage.Contains("Database", StringComparison.OrdinalIgnoreCase))
        {
            return "data";
        }

        if (stage.Contains("DI", StringComparison.OrdinalIgnoreCase)
            || stage.Contains("Override", StringComparison.OrdinalIgnoreCase)
            || stage.Contains("Base class", StringComparison.OrdinalIgnoreCase))
        {
            return "handoff";
        }

        if (stage.Contains("External", StringComparison.OrdinalIgnoreCase)
            || stage.Contains("boundary", StringComparison.OrdinalIgnoreCase)
            || stage.Contains("Unresolved", StringComparison.OrdinalIgnoreCase))
        {
            return "boundary";
        }

        if (stage.Contains("API", StringComparison.OrdinalIgnoreCase))
        {
            return "entry";
        }

        return "app";
    }

    private static bool IsFrameworkOrExternalStep(CSharpBackendTraceStep step)
    {
        if (step.Stage.Equals("External/library call", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var identifier = step.TargetIdentifier ?? step.Title;
        var targetType = ReceiverTypeFromIdentifier(identifier);
        return targetType is not null && IsFrameworkType(targetType);
    }

    private static bool IsFrameworkType(string typeName) =>
        typeName.StartsWith("System.", StringComparison.Ordinal)
        || typeName is "HashSet<string>" or "byte[]" or "string" or "HttpClient" or "Task" or "IEnumerable" or "IQueryable"
        || typeName.StartsWith("List<", StringComparison.Ordinal)
        || typeName.StartsWith("Dictionary<", StringComparison.Ordinal)
        || typeName.StartsWith("Mock<", StringComparison.Ordinal)
        || typeName.StartsWith("ILogger", StringComparison.Ordinal);

    private static string? PreferredSourceIdentifier(CSharpBackendTraceStep step)
    {
        if (IsTraceableMethod(step.TargetIdentifier))
        {
            return step.TargetIdentifier;
        }

        if (IsTraceableMethod(step.SourceIdentifier))
        {
            return step.SourceIdentifier;
        }

        return step.TargetIdentifier ?? step.SourceIdentifier;
    }

    private static bool IsTraceableMethod(string? identifier) =>
        !string.IsNullOrWhiteSpace(identifier)
        && identifier.Contains("::", StringComparison.Ordinal)
        && !identifier.StartsWith("method:I", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractRelativePath(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }

        var markerIndex = identifier.LastIndexOf("::", StringComparison.Ordinal);
        return markerIndex <= 0 ? null : identifier[..markerIndex];
    }

    private static int? ResolveLineNumber(
        string? identifier,
        string? metadata,
        IReadOnlyDictionary<string, int?> symbolLines)
    {
        var metadataLine = MetadataIntValue(metadata, "line");
        if (metadataLine is not null)
        {
            return metadataLine;
        }

        if (!string.IsNullOrWhiteSpace(identifier)
            && symbolLines.TryGetValue(identifier, out var indexedLine))
        {
            return indexedLine;
        }

        return null;
    }

    private static string? ReadSourcePreview(string rootPath, string? relativePath, int? lineNumber)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || lineNumber is null or <= 0)
        {
            return null;
        }

        var fullPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        if (!fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
        {
            return null;
        }

        try
        {
            return File.ReadLines(fullPath).Skip(lineNumber.Value - 1).FirstOrDefault()?.Trim();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ReceiverTypeFromIdentifier(string? targetIdentifier)
    {
        if (string.IsNullOrWhiteSpace(targetIdentifier))
        {
            return null;
        }

        var value = targetIdentifier;
        var markerIndex = value.LastIndexOf("::", StringComparison.Ordinal);
        if (markerIndex >= 0)
        {
            value = value[(markerIndex + 2)..];
        }

        var separatorIndex = value.LastIndexOf('.');
        return separatorIndex <= 0 ? null : value[..separatorIndex];
    }

    private static int? MetadataIntValue(string? metadata, string key)
    {
        var value = MetadataValue(metadata, key);
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string? MetadataValue(string? metadata, string key)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return null;
        }

        foreach (var part in metadata.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = part.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            if (part[..separatorIndex].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return part[(separatorIndex + 1)..];
            }
        }

        return null;
    }

    private sealed record ResolvedCall(
        string Stage,
        string Title,
        string Detail,
        string Confidence,
        string Reason,
        string? NextMethod,
        string? RuntimeClass);

    private sealed record TraceEntry(CodeRelationship? Relationship, string TargetMethodIdentifier);

    private sealed class CSharpTraceGraph
    {
        private static readonly HashSet<string> TraversableKinds = new(StringComparer.Ordinal)
        {
            "calls_method",
            "executes_procedure",
            "reads_table",
            "writes_table",
            "saves_changes"
        };

        private readonly IReadOnlyList<CodeRelationship> _relationships;
        private readonly Dictionary<string, List<CodeRelationship>> _outgoing;
        private readonly Dictionary<string, List<CodeRelationship>> _containsByClass;
        private readonly Dictionary<string, List<CodeRelationship>> _methodsByName;
        private readonly Dictionary<string, CodeRelationship> _classByName;
        private readonly Dictionary<string, string> _baseClassByClass;

        private CSharpTraceGraph(
            IReadOnlyList<CodeRelationship> relationships,
            Dictionary<string, List<CodeRelationship>> outgoing,
            Dictionary<string, List<CodeRelationship>> containsByClass,
            Dictionary<string, List<CodeRelationship>> methodsByName,
            Dictionary<string, CodeRelationship> classByName,
            Dictionary<string, string> baseClassByClass)
        {
            _relationships = relationships;
            _outgoing = outgoing;
            _containsByClass = containsByClass;
            _methodsByName = methodsByName;
            _classByName = classByName;
            _baseClassByClass = baseClassByClass;
        }

        public static CSharpTraceGraph Create(IReadOnlyList<CodeRelationship> relationships)
        {
            var outgoing = relationships
                .Where(x => TraversableKinds.Contains(x.RelationshipKind))
                .GroupBy(x => x.SourceIdentifier, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);

            var containsByClass = relationships
                .Where(x => x.RelationshipKind == "contains_symbol" && x.TargetKind == "method")
                .GroupBy(x => x.SourceIdentifier, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);

            var methodsByName = relationships
                .Where(x => x.RelationshipKind == "contains_symbol" && x.TargetKind == "method")
                .GroupBy(x => MemberName(x.TargetIdentifier), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);

            var classByName = relationships
                .Where(x => x.RelationshipKind == "contains_symbol" && x.TargetKind is "class" or "controller" or "repository")
                .GroupBy(x => MemberName(x.TargetIdentifier), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            var baseClassByClass = relationships
                .Where(x => x.RelationshipKind == "inherits_from")
                .GroupBy(x => x.SourceIdentifier, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => MemberName(x.First().TargetIdentifier), StringComparer.OrdinalIgnoreCase);

            return new CSharpTraceGraph(relationships, outgoing, containsByClass, methodsByName, classByName, baseClassByClass);
        }

        public TraceEntry? FindEntry(string entry)
        {
            var normalizedEntry = NormalizeSearch(entry);
            var apiEntry = _relationships
                .Where(x => x.RelationshipKind == "handled_by" && x.SourceKind == "api")
                .Where(x => NormalizeSearch(x.SourceIdentifier).Contains(normalizedEntry, StringComparison.OrdinalIgnoreCase)
                    || normalizedEntry.Contains(NormalizeSearch(x.SourceIdentifier), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => NormalizeSearch(x.SourceIdentifier).Equals(normalizedEntry, StringComparison.OrdinalIgnoreCase))
                .ThenBy(x => x.SourceIdentifier.Length)
                .FirstOrDefault();
            if (apiEntry is not null)
            {
                return new TraceEntry(apiEntry, apiEntry.TargetIdentifier);
            }

            var methodEntry = _relationships
                .Where(x => x.RelationshipKind == "contains_symbol" && x.TargetKind == "method")
                .Where(x => NormalizeSearch(x.TargetIdentifier).Contains(normalizedEntry, StringComparison.OrdinalIgnoreCase)
                    || NormalizeSearch(FormatIdentifier(x.TargetIdentifier)).Contains(normalizedEntry, StringComparison.OrdinalIgnoreCase)
                    || MemberName(x.TargetIdentifier).Equals(entry.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => NormalizeSearch(x.TargetIdentifier).Equals(normalizedEntry, StringComparison.OrdinalIgnoreCase))
                .ThenBy(x => x.TargetIdentifier.Length)
                .FirstOrDefault();

            return methodEntry is null ? null : new TraceEntry(null, methodEntry.TargetIdentifier);
        }

        public void TraceMethod(
            string sourceIdentifier,
            string? runtimeClass,
            int maxSteps,
            List<CSharpBackendTraceStep> steps,
            List<string> warnings,
            HashSet<string> visiting,
            CancellationToken cancellationToken)
        {
            if (steps.Count >= maxSteps)
            {
                return;
            }

            if (!visiting.Add(sourceIdentifier))
            {
                warnings.Add($"Skipped recursive call back into {sourceIdentifier}.");
                return;
            }

            foreach (var next in GetExecutionRelationships(sourceIdentifier))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (steps.Count >= maxSteps)
                {
                    warnings.Add($"Stopped after {maxSteps} trace steps. Increase depth to inspect more calls.");
                    break;
                }

                if (next.RelationshipKind is "executes_procedure" or "reads_table" or "writes_table" or "saves_changes")
                {
                    AddStep(
                        steps,
                        StageForRelationship(next.RelationshipKind),
                        FormatRelationshipTitle(next),
                        FormatRelationshipDetail(next),
                        "confirmed",
                        "Found direct data access evidence in the method body.",
                        next);
                    continue;
                }

                var resolved = ResolveCall(sourceIdentifier, next, runtimeClass);
                AddStep(
                    steps,
                    resolved.Stage,
                    resolved.Title,
                    resolved.Detail,
                    resolved.Confidence,
                    resolved.Reason,
                    next);

                if (resolved.NextMethod is null)
                {
                    if (resolved.Stage.Equals("DI boundary", StringComparison.OrdinalIgnoreCase))
                    {
                        warnings.Add($"Reached {FormatIdentifier(next.TargetIdentifier)}, but no concrete implementation source was indexed.");
                    }
                    else if (resolved.Stage.Equals("External/library call", StringComparison.OrdinalIgnoreCase))
                    {
                        warnings.Add($"Stopped at external/library call {FormatIdentifier(next.TargetIdentifier)}.");
                    }
                    else
                    {
                        warnings.Add($"Could not resolve the next method after {FormatIdentifier(next.TargetIdentifier)}.");
                    }
                    continue;
                }

                TraceMethod(
                    resolved.NextMethod,
                    resolved.RuntimeClass ?? runtimeClass,
                    maxSteps,
                    steps,
                    warnings,
                    visiting,
                    cancellationToken);
            }

            visiting.Remove(sourceIdentifier);
        }

        private IEnumerable<CodeRelationship> GetExecutionRelationships(string sourceIdentifier)
        {
            if (_outgoing.TryGetValue(sourceIdentifier, out var outgoing))
            {
                return outgoing.OrderBy(x => x.Id);
            }

            var methodName = MemberName(sourceIdentifier);
            return _outgoing.TryGetValue(methodName, out outgoing)
                ? outgoing.OrderBy(x => x.Id)
                : [];
        }

        public ResolvedCall ResolveCall(string currentMethod, CodeRelationship call, string? runtimeClass)
        {
            var targetMethodName = MemberName(call.TargetIdentifier);
            if (TryResolveInjectedCall(currentMethod, call, targetMethodName, runtimeClass, out var injected))
            {
                return injected;
            }

            if (runtimeClass is not null
                && TryResolveClassMethod(runtimeClass, targetMethodName, out var runtimeMethod))
            {
                return new ResolvedCall(
                    "Override call",
                    $"Enter {FormatIdentifier(runtimeMethod)}",
                    $"{FormatIdentifier(call.SourceIdentifier)} dispatches to the runtime implementation {FormatIdentifier(runtimeClass)}.",
                    "confirmed",
                    "Resolved by keeping the concrete implementation selected by DI.",
                    runtimeMethod,
                    runtimeClass);
            }

            var currentClass = FindContainingClass(currentMethod);
            if (currentClass is not null
                && TryResolveClassMethod(currentClass, targetMethodName, out var sameClassMethod))
            {
                return new ResolvedCall(
                    "Method call",
                    $"Enter {FormatIdentifier(sameClassMethod)}",
                    $"{FormatIdentifier(call.SourceIdentifier)} calls a method in the same class.",
                    "confirmed",
                    "Found a matching method in the current class.",
                    sameClassMethod,
                    runtimeClass);
            }

            if (currentClass is not null
                && TryResolveBaseClassMethod(currentClass, targetMethodName, out var baseMethod))
            {
                return new ResolvedCall(
                    "Base class call",
                    $"Enter {FormatIdentifier(baseMethod)}",
                    $"{FormatIdentifier(call.SourceIdentifier)} calls an inherited method.",
                    "confirmed",
                    "Resolved through the class inheritance relationship.",
                    baseMethod,
                    runtimeClass);
            }

            if (_methodsByName.TryGetValue(targetMethodName, out var candidates) && candidates.Count == 1)
            {
                var candidateClass = FindContainingClass(candidates[0].TargetIdentifier);
                var isBaseClassCall = currentClass is not null
                    && MetadataValue(call.Metadata, "direct")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true
                    && (candidateClass is null
                        || !candidateClass.Equals(currentClass, StringComparison.OrdinalIgnoreCase));

                return new ResolvedCall(
                    isBaseClassCall ? "Base class call" : "Method call",
                    $"Enter {FormatIdentifier(candidates[0].TargetIdentifier)}",
                    isBaseClassCall
                        ? $"{FormatIdentifier(call.SourceIdentifier)} calls an inherited method."
                        : $"{FormatIdentifier(call.SourceIdentifier)} calls {targetMethodName}.",
                    isBaseClassCall ? "confirmed" : "likely",
                    isBaseClassCall
                        ? "Resolved through the class inheritance relationship."
                        : "Only one method with this name was indexed.",
                    candidates[0].TargetIdentifier,
                    runtimeClass);
            }

            var nameOnlyCandidates = _relationships
                .Where(x => x.RelationshipKind == "contains_symbol"
                    && x.TargetKind == "method"
                    && MemberName(x.TargetIdentifier).Equals(targetMethodName, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.TargetIdentifier)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (nameOnlyCandidates.Count == 1)
            {
                var candidateClass = FindContainingClass(nameOnlyCandidates[0]);
                var isBaseClassCall = currentClass is not null
                    && MetadataValue(call.Metadata, "direct")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true
                    && (candidateClass is null
                        || !candidateClass.Equals(currentClass, StringComparison.OrdinalIgnoreCase));

                return new ResolvedCall(
                    isBaseClassCall ? "Base class call" : "Method call",
                    $"Enter {FormatIdentifier(nameOnlyCandidates[0])}",
                    isBaseClassCall
                        ? $"{FormatIdentifier(call.SourceIdentifier)} calls an inherited method."
                        : $"{FormatIdentifier(call.SourceIdentifier)} calls {targetMethodName}.",
                    isBaseClassCall ? "confirmed" : "likely",
                    isBaseClassCall
                        ? "Resolved through the class inheritance relationship."
                        : "Found one indexed method with this name.",
                    nameOnlyCandidates[0],
                    runtimeClass);
            }

            return new ResolvedCall(
                "Unresolved call",
                $"Calls {FormatIdentifier(call.TargetIdentifier)}",
                FormatRelationshipDetail(call),
                "unknown",
                "No exact method, DI registration, or inheritance match was found.",
                null,
                runtimeClass);
        }

        private bool TryResolveInjectedCall(
            string currentMethod,
            CodeRelationship call,
            string targetMethodName,
            string? runtimeClass,
            out ResolvedCall resolved)
        {
            resolved = default!;
            var receiver = MetadataValue(call.Metadata, "receiver");
            var targetType = ReceiverType(call.TargetIdentifier);
            if (targetType is null)
            {
                return false;
            }

            var currentClass = FindContainingClass(currentMethod);
            var candidateClasses = GetDependencySearchClasses(currentClass, runtimeClass);
            if (candidateClasses.Count == 0)
            {
                return false;
            }

            var dependencies = _relationships
                .Where(x => x.RelationshipKind == "depends_on"
                    && candidateClasses.Any(candidate => x.SourceIdentifier.Equals(candidate, StringComparison.OrdinalIgnoreCase)
                        || MemberName(x.SourceIdentifier).Equals(MemberName(candidate), StringComparison.OrdinalIgnoreCase))
                    && MemberName(x.TargetIdentifier).Equals(targetType, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!string.IsNullOrWhiteSpace(receiver))
            {
                dependencies = dependencies
                    .Where(x => MetadataValue(x.Metadata, "field")?.Equals(receiver, StringComparison.OrdinalIgnoreCase) == true
                        || MetadataValue(x.Metadata, "parameter")?.Equals(receiver, StringComparison.OrdinalIgnoreCase) == true
                        || x.Metadata is null)
                    .ToList();
            }

            var dependency = dependencies.FirstOrDefault(x => MetadataValue(x.Metadata, "key") is not null)
                ?? dependencies.FirstOrDefault();
            if (dependency is null && !LooksLikeInterface(targetType))
            {
                return false;
            }

            var key = MetadataValue(dependency?.Metadata, "key");
            var serviceIdentifier = dependency?.TargetIdentifier ?? targetType;
            var implementations = _relationships
                .Where(x => x.RelationshipKind == "implemented_by"
                    && (x.SourceIdentifier.Equals(serviceIdentifier, StringComparison.OrdinalIgnoreCase)
                        || MemberName(x.SourceIdentifier).Equals(targetType, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (!string.IsNullOrWhiteSpace(key))
            {
                implementations = implementations
                    .Where(x => MetadataValue(x.Metadata, "key")?.Equals(key, StringComparison.OrdinalIgnoreCase) == true)
                    .ToList();
            }

            var implementation = implementations.FirstOrDefault();
            var implementationClass = implementation is null
                ? InferImplementationClass(targetType, targetMethodName)
                : MemberName(implementation.TargetIdentifier);
            if (implementationClass is null)
            {
                var looksLikeInterface = LooksLikeInterface(targetType);
                resolved = new ResolvedCall(
                    looksLikeInterface ? "DI boundary" : "External/library call",
                    $"Calls {targetType}.{targetMethodName}",
                    $"{FormatIdentifier(call.SourceIdentifier)} calls {targetMethodName} on {receiver ?? targetType}.",
                    "unknown",
                    looksLikeInterface
                        ? "The interface call is source-backed, but no concrete implementation was found in the indexed code."
                        : "The call is source-backed, but it points into framework, SDK, or package code outside the indexed repository.",
                    null,
                    runtimeClass);
                return true;
            }

            key ??= MetadataValue(implementation?.Metadata, "key");
            if (!TryResolveClassMethod(implementationClass, targetMethodName, out var targetMethod))
            {
                return false;
            }

            resolved = new ResolvedCall(
                string.IsNullOrWhiteSpace(key) ? "DI handoff" : "Keyed DI handoff",
                $"{targetType} resolves to {implementationClass}",
                $"{FormatIdentifier(call.SourceIdentifier)} calls {targetMethodName} on {receiver ?? targetType}.",
                "confirmed",
                string.IsNullOrWhiteSpace(key)
                    ? "Resolved through dependency injection registration."
                    : $"Resolved through keyed dependency injection using key '{key}'.",
                targetMethod,
                implementationClass);
            return true;
        }

        private List<string> GetDependencySearchClasses(string? currentClass, string? runtimeClass)
        {
            var candidates = new List<string>();
            AddClassCandidate(currentClass, candidates);
            AddClassCandidate(runtimeClass, candidates);

            foreach (var candidate in candidates.ToList())
            {
                AddClassCandidate(FindBaseClass(candidate), candidates);
            }

            foreach (var derived in _relationships
                .Where(x => x.RelationshipKind == "inherits_from"
                    && candidates.Any(candidate => MemberName(x.TargetIdentifier).Equals(MemberName(candidate), StringComparison.OrdinalIgnoreCase)))
                .Select(x => x.SourceIdentifier))
            {
                AddClassCandidate(derived, candidates);
            }

            return candidates;
        }

        private static void AddClassCandidate(string? classNameOrIdentifier, List<string> candidates)
        {
            if (string.IsNullOrWhiteSpace(classNameOrIdentifier)
                || candidates.Any(x => x.Equals(classNameOrIdentifier, StringComparison.OrdinalIgnoreCase)
                    || MemberName(x).Equals(MemberName(classNameOrIdentifier), StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            candidates.Add(classNameOrIdentifier);
        }

        private string? FindBaseClass(string currentClass)
        {
            if (_baseClassByClass.TryGetValue(currentClass, out var baseClassName))
            {
                return baseClassName;
            }

            return _relationships
                .Where(x => x.RelationshipKind == "inherits_from"
                    && MemberName(x.SourceIdentifier).Equals(MemberName(currentClass), StringComparison.OrdinalIgnoreCase))
                .Select(x => MemberName(x.TargetIdentifier))
                .FirstOrDefault();
        }

        private string? InferImplementationClass(string targetType, string targetMethodName)
        {
            var inferredClass = targetType.StartsWith('I') && targetType.Length > 1 && char.IsUpper(targetType[1])
                ? targetType[1..]
                : targetType;

            if (TryResolveClassMethod(inferredClass, targetMethodName, out _))
            {
                return inferredClass;
            }

            return _containsByClass.Keys
                .Select(MemberName)
                .Where(x => x.Equals(inferredClass, StringComparison.OrdinalIgnoreCase)
                    || x.EndsWith(inferredClass, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(x => TryResolveClassMethod(x, targetMethodName, out _));
        }

        private static bool LooksLikeInterface(string typeName) =>
            typeName.StartsWith('I') && typeName.Length > 1 && char.IsUpper(typeName[1]);

        private string? FindContainingClass(string methodIdentifier)
        {
            return _relationships
                .Where(x => x.RelationshipKind == "contains_symbol"
                    && x.TargetKind == "method"
                    && x.TargetIdentifier.Equals(methodIdentifier, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.SourceIdentifier)
                .FirstOrDefault();
        }

        private bool TryResolveBaseClassMethod(string currentClass, string methodName, out string methodIdentifier)
        {
            methodIdentifier = string.Empty;
            if (!_baseClassByClass.TryGetValue(currentClass, out var baseClassName))
            {
                baseClassName = _relationships
                    .Where(x => x.RelationshipKind == "inherits_from"
                        && MemberName(x.SourceIdentifier).Equals(MemberName(currentClass), StringComparison.OrdinalIgnoreCase))
                    .Select(x => MemberName(x.TargetIdentifier))
                    .FirstOrDefault();
                if (baseClassName is null)
                {
                    return false;
                }
            }

            return TryResolveClassMethod(baseClassName, methodName, out methodIdentifier);
        }

        private bool IsBaseClass(string currentClass, string candidateClass)
        {
            if (!_baseClassByClass.TryGetValue(currentClass, out var baseClassName))
            {
                baseClassName = _relationships
                    .Where(x => x.RelationshipKind == "inherits_from"
                        && MemberName(x.SourceIdentifier).Equals(MemberName(currentClass), StringComparison.OrdinalIgnoreCase))
                    .Select(x => MemberName(x.TargetIdentifier))
                    .FirstOrDefault();
            }

            return baseClassName is not null
                && MemberName(candidateClass).Equals(baseClassName, StringComparison.OrdinalIgnoreCase);
        }

        private bool HasBaseClass(string currentClass) =>
            _baseClassByClass.ContainsKey(currentClass)
            || _relationships.Any(x => x.RelationshipKind == "inherits_from"
                && MemberName(x.SourceIdentifier).Equals(MemberName(currentClass), StringComparison.OrdinalIgnoreCase));

        private bool TryResolveClassMethod(string classNameOrIdentifier, string methodName, out string methodIdentifier)
        {
            methodIdentifier = string.Empty;
            var classIdentifier = classNameOrIdentifier.Contains("::", StringComparison.Ordinal)
                ? classNameOrIdentifier
                : _classByName.TryGetValue(classNameOrIdentifier, out var classRelationship)
                    ? classRelationship.TargetIdentifier
                    : _relationships
                        .Where(x => x.RelationshipKind == "contains_symbol"
                            && x.TargetKind is "class" or "controller" or "repository"
                            && MemberName(x.TargetIdentifier).Equals(classNameOrIdentifier, StringComparison.OrdinalIgnoreCase))
                        .Select(x => x.TargetIdentifier)
                        .FirstOrDefault();

            if (classIdentifier is null)
            {
                return false;
            }

            if (!_containsByClass.TryGetValue(classIdentifier, out var methods))
            {
                methods = _relationships
                    .Where(x => x.RelationshipKind == "contains_symbol"
                        && x.TargetKind == "method"
                        && MemberName(x.SourceIdentifier).Equals(MemberName(classIdentifier), StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (methods.Count == 0)
                {
                    return false;
                }
            }

            var method = methods.FirstOrDefault(x => MemberName(x.TargetIdentifier).Equals(methodName, StringComparison.OrdinalIgnoreCase));
            if (method is null)
            {
                return false;
            }

            methodIdentifier = method.TargetIdentifier;
            return true;
        }

        private static string? ReceiverType(string targetIdentifier)
        {
            var value = StripPathPrefix(targetIdentifier);
            var separatorIndex = value.LastIndexOf('.');
            return separatorIndex <= 0 ? null : value[..separatorIndex];
        }

        private static string MemberName(string identifier)
        {
            var value = StripPathPrefix(identifier);
            var dotIndex = value.LastIndexOf('.');
            return dotIndex >= 0 ? value[(dotIndex + 1)..] : value;
        }

        private static string StripPathPrefix(string identifier)
        {
            var value = identifier;
            var markerIndex = value.LastIndexOf("::", StringComparison.Ordinal);
            if (markerIndex >= 0)
            {
                value = value[(markerIndex + 2)..];
            }

            var globalIndex = value.LastIndexOf("global::", StringComparison.Ordinal);
            if (globalIndex >= 0)
            {
                value = value[(globalIndex + "global::".Length)..];
            }

            return value;
        }

        private static string NormalizeSearch(string value) =>
            System.Text.RegularExpressions.Regex.Replace(
                value.Trim().Trim('/'),
                "\\{([^}:]+):[^}]+\\}",
                "{$1}");

        private static string? MetadataValue(string? metadata, string key)
        {
            if (string.IsNullOrWhiteSpace(metadata))
            {
                return null;
            }

            foreach (var part in metadata.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separatorIndex = part.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                if (part[..separatorIndex].Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    return part[(separatorIndex + 1)..];
                }
            }

            return null;
        }
    }
}
