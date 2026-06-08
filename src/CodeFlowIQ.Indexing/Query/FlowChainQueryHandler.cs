using CodeFlowIQ.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CodeFlowIQ.Indexing;

public sealed class FlowChainQueryHandler
{
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
            query = WorkspaceQueryFilters.ExcludeTestRelationships(query);
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
            relationshipQuery = WorkspaceQueryFilters.ExcludeTestRelationships(relationshipQuery);
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
