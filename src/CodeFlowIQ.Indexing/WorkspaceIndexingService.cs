using System.Security.Cryptography;
using CodeFlowIQ.Core.Analysis;
using CodeFlowIQ.Core.Git;
using CodeFlowIQ.Core.Indexing;
using CodeFlowIQ.Core.Models;
using CodeFlowIQ.Data;
using Microsoft.EntityFrameworkCore;

namespace CodeFlowIQ.Indexing;

public sealed class WorkspaceIndexingService(
    IGitWorkspaceDetector gitDetector,
    ILanguageDetector languageDetector,
    IEnumerable<ILanguageAnalyzer> analyzers,
    IndexingOptions options) : IWorkspaceIndexingService
{
    private const string DefaultAnalysisSchemaVersion = "analysis-v1";
    private const string CSharpAnalysisSchemaVersion = "csharp-analysis-v2";
    private readonly IReadOnlyList<ILanguageAnalyzer> _analyzers = analyzers.ToList();

    public Task<IndexingSummary> InitializeAsync(
        string workspacePath,
        CancellationToken cancellationToken,
        IProgress<IndexingProgress>? progress = null) =>
        IndexAsync(workspacePath, cancellationToken, progress);

    public Task<IndexingSummary> SyncAsync(
        string workspacePath,
        CancellationToken cancellationToken,
        IProgress<IndexingProgress>? progress = null) =>
        IndexAsync(workspacePath, cancellationToken, progress);

    private async Task<IndexingSummary> IndexAsync(
        string workspacePath,
        CancellationToken cancellationToken,
        IProgress<IndexingProgress>? progress)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var rootPath = Path.GetFullPath(workspacePath);
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Workspace path does not exist: {rootPath}");
        }

        progress?.Report(new IndexingProgress("Preparing", 0, 0, 0, 0, null, "Preparing workspace index."));

        await using var db = await WorkspaceDatabase.OpenMigratedAsync(rootPath, cancellationToken);
        var ignoreRules = IgnoreRuleSet.Load(rootPath, options);

        var gitInfo = gitDetector.Detect(rootPath);
        var workspace = await db.Workspaces.FirstOrDefaultAsync(x => x.RootPath == rootPath, cancellationToken);
        if (workspace is null)
        {
            workspace = new Workspace
            {
                Name = new DirectoryInfo(rootPath).Name,
                RootPath = rootPath,
                CreatedAt = startedAt,
                UpdatedAt = startedAt
            };
            db.Workspaces.Add(workspace);
        }

        workspace.Kind = gitInfo.IsGitRepository ? WorkspaceKind.GitRepository : WorkspaceKind.PlainDirectory;
        workspace.GitRootPath = gitInfo.GitRootPath;
        workspace.CurrentBranch = gitInfo.CurrentBranch;
        workspace.HeadCommitSha = gitInfo.HeadCommitSha;
        workspace.UpdatedAt = startedAt;

        await db.SaveChangesAsync(cancellationToken);

        var existingFiles = await db.IndexedFiles
            .Include(x => x.Symbols)
            .Where(x => x.WorkspaceId == workspace.Id)
            .ToDictionaryAsync(x => x.RelativePath, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filesScanned = 0;
        var filesIndexed = 0;
        var filesSkipped = 0;
        var symbolsIndexed = 0;

        foreach (var filePath in EnumerateWorkspaceFiles(rootPath, ignoreRules))
        {
            cancellationToken.ThrowIfCancellationRequested();
            filesScanned++;

            var fileInfo = new FileInfo(filePath);
            var relativePath = Path.GetRelativePath(rootPath, filePath);
            seenPaths.Add(relativePath);

            if (filesScanned == 1 || filesScanned % 100 == 0)
            {
                progress?.Report(new IndexingProgress(
                    "Scanning",
                    filesScanned,
                    filesIndexed,
                    filesSkipped,
                    symbolsIndexed,
                    relativePath,
                    $"Scanned {filesScanned} files."));
            }

            if (fileInfo.Length > options.MaxFileSizeKb * 1024L || IsGeneratedFile(fileInfo.Name))
            {
                filesSkipped++;
                continue;
            }

            var languageId = languageDetector.Detect(filePath);
            var fileHash = await ComputeSha256Async(filePath, cancellationToken);
            var contentHash = CreateIndexedContentHash(languageId, fileHash);

            if (existingFiles.TryGetValue(relativePath, out var existing)
                && existing.ContentHash == contentHash
                && existing.IsDeleted == false)
            {
                continue;
            }

            progress?.Report(new IndexingProgress(
                "Indexing",
                filesScanned,
                filesIndexed,
                filesSkipped,
                symbolsIndexed,
                relativePath,
                $"Indexing {relativePath}."));

            var indexedFile = existing ?? new IndexedFile
            {
                WorkspaceId = workspace.Id,
                RelativePath = relativePath,
                FullPath = filePath,
                LanguageId = languageId,
                ContentHash = contentHash
            };

            indexedFile.FullPath = filePath;
            indexedFile.LanguageId = languageId;
            indexedFile.ContentHash = contentHash;
            indexedFile.SizeBytes = fileInfo.Length;
            indexedFile.LastWriteTimeUtc = fileInfo.LastWriteTimeUtc;
            indexedFile.IndexedAt = DateTimeOffset.UtcNow;
            indexedFile.IsDeleted = false;

            indexedFile.Symbols.Clear();
            RemoveRelationshipsForFile(db, workspace.Id, relativePath);
            var analysisResult = await AnalyzeAsync(filePath, cancellationToken);
            indexedFile.ParseStatus = analysisResult.Status;
            indexedFile.ParseError = analysisResult.Error;

            foreach (var symbol in analysisResult.Symbols)
            {
                indexedFile.Symbols.Add(new CodeSymbol
                {
                    Name = symbol.Name,
                    Kind = symbol.Kind,
                    ContainerName = symbol.ContainerName,
                    LineNumber = symbol.LineNumber,
                    ColumnNumber = symbol.ColumnNumber
                });

                db.CodeRelationships.Add(CreateRelationship(
                    workspace.Id,
                    "file",
                    relativePath,
                    "contains_symbol",
                    symbol.Kind,
                    $"{relativePath}::{symbol.Name}",
                    $"line={symbol.LineNumber};column={symbol.ColumnNumber}"));

                if (!string.IsNullOrWhiteSpace(symbol.ContainerName))
                {
                    db.CodeRelationships.Add(CreateRelationship(
                        workspace.Id,
                        "symbol",
                        $"{relativePath}::{symbol.ContainerName}",
                        "contains_symbol",
                        symbol.Kind,
                        $"{relativePath}::{symbol.Name}",
                        null));
                }
            }

            foreach (var relationship in analysisResult.Relationships)
            {
                db.CodeRelationships.Add(CreateRelationship(
                    workspace.Id,
                    relationship.SourceKind,
                    QualifyIdentifier(relativePath, relationship.SourceIdentifier),
                    relationship.RelationshipKind,
                    relationship.TargetKind,
                    QualifyIdentifier(relativePath, relationship.TargetIdentifier),
                    relationship.Metadata));
            }

            symbolsIndexed += indexedFile.Symbols.Count;

            if (existing is null)
            {
                db.IndexedFiles.Add(indexedFile);
            }

            filesIndexed++;

            progress?.Report(new IndexingProgress(
                "Indexing",
                filesScanned,
                filesIndexed,
                filesSkipped,
                symbolsIndexed,
                relativePath,
                $"Indexed {filesIndexed} changed files."));
        }

        foreach (var indexedFile in existingFiles.Values.Where(x => !seenPaths.Contains(x.RelativePath)))
        {
            indexedFile.IsDeleted = true;
            indexedFile.IndexedAt = DateTimeOffset.UtcNow;
            RemoveRelationshipsForFile(db, workspace.Id, indexedFile.RelativePath);
        }

        progress?.Report(new IndexingProgress(
            "Finalizing",
            filesScanned,
            filesIndexed,
            filesSkipped,
            symbolsIndexed,
            null,
            "Finalizing relationships."));

        workspace.LastIndexedAt = DateTimeOffset.UtcNow;
        workspace.UpdatedAt = workspace.LastIndexedAt.Value;
        await db.SaveChangesAsync(cancellationToken);
        await ResolveFrontendHandlerRelationshipsAsync(db, workspace.Id, cancellationToken);
        await ResolveCrossStackApiRelationshipsAsync(db, workspace.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var summary = new IndexingSummary(
            workspace.Id,
            workspace.Name,
            filesScanned,
            filesIndexed,
            filesSkipped,
            symbolsIndexed,
            startedAt,
            DateTimeOffset.UtcNow);

        progress?.Report(new IndexingProgress(
            "Completed",
            filesScanned,
            filesIndexed,
            filesSkipped,
            symbolsIndexed,
            null,
            "Workspace index is ready."));

        return summary;
    }

    private async Task<CodeAnalysisResult> AnalyzeAsync(string filePath, CancellationToken cancellationToken)
    {
        var analyzer = _analyzers.FirstOrDefault(x => x.CanAnalyze(filePath));
        if (analyzer is null)
        {
            return new CodeAnalysisResult("unknown", "not-supported", [], []);
        }

        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        return await analyzer.AnalyzeAsync(filePath, content, cancellationToken);
    }

    private IEnumerable<string> EnumerateWorkspaceFiles(string rootPath, IgnoreRuleSet ignoreRules)
    {
        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            IEnumerable<string> directories;
            IEnumerable<string> files;
            try
            {
                directories = Directory.EnumerateDirectories(current);
                files = Directory.EnumerateFiles(current);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var directory in directories)
            {
                var relativeDirectory = Path.GetRelativePath(rootPath, directory);
                if (!ShouldSkipDirectory(directory) && !ignoreRules.IsIgnored(relativeDirectory, isDirectory: true))
                {
                    pending.Push(directory);
                }
            }

            foreach (var file in files)
            {
                var relativeFile = Path.GetRelativePath(rootPath, file);
                if (!ignoreRules.IsIgnored(relativeFile, isDirectory: false))
                {
                    yield return file;
                }
            }
        }
    }

    private bool ShouldSkipDirectory(string directoryPath)
    {
        var name = Path.GetFileName(directoryPath);
        return options.ExcludedDirectories.Any(x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsGeneratedFile(string fileName) =>
        options.SkipGeneratedFiles
        && (fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase));

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static string CreateIndexedContentHash(string languageId, string fileHash) =>
        $"{GetAnalysisSchemaVersion(languageId)}:{fileHash}";

    private static string GetAnalysisSchemaVersion(string languageId) =>
        languageId.Equals("csharp", StringComparison.OrdinalIgnoreCase)
            ? CSharpAnalysisSchemaVersion
            : DefaultAnalysisSchemaVersion;

    private static CodeRelationship CreateRelationship(
        int workspaceId,
        string sourceKind,
        string sourceIdentifier,
        string relationshipKind,
        string targetKind,
        string targetIdentifier,
        string? metadata) =>
        new()
        {
            WorkspaceId = workspaceId,
            SourceKind = sourceKind,
            SourceIdentifier = sourceIdentifier,
            RelationshipKind = relationshipKind,
            TargetKind = targetKind,
            TargetIdentifier = targetIdentifier,
            Metadata = metadata,
            DiscoveredAt = DateTimeOffset.UtcNow
        };

    private static string QualifyIdentifier(string relativePath, string identifier) =>
        identifier.StartsWith("global::", StringComparison.Ordinal)
            ? identifier["global::".Length..]
            : identifier.Contains(" ", StringComparison.Ordinal)
            || identifier.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || identifier.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || identifier.StartsWith("/", StringComparison.Ordinal)
            ? identifier
            : $"{relativePath}::{identifier}";

    private static void RemoveRelationshipsForFile(CodeFlowIqDbContext db, int workspaceId, string relativePath)
    {
        var prefix = relativePath + "::";
        var relationships = db.CodeRelationships.Where(x =>
            x.WorkspaceId == workspaceId
            && (x.SourceIdentifier == relativePath
                || x.SourceIdentifier.StartsWith(prefix)
                || x.TargetIdentifier == relativePath
                || x.TargetIdentifier.StartsWith(prefix)));

        db.CodeRelationships.RemoveRange(relationships);
    }

    private static async Task ResolveFrontendHandlerRelationshipsAsync(
        CodeFlowIqDbContext db,
        int workspaceId,
        CancellationToken cancellationToken)
    {
        var handlerRelationships = await db.CodeRelationships
            .Where(x => x.WorkspaceId == workspaceId
                && x.RelationshipKind == "invokes_handler"
                && x.SourceKind == "ui-event"
                && x.TargetKind == "method")
            .ToListAsync(cancellationToken);

        foreach (var relationship in handlerRelationships)
        {
            if (!TrySplitQualifiedIdentifier(relationship.SourceIdentifier, out var sourcePath, out _)
                || !TrySplitQualifiedIdentifier(relationship.TargetIdentifier, out _, out var handlerName))
            {
                continue;
            }

            var componentStem = GetComponentStem(sourcePath);
            var matchingMethod = await db.CodeSymbols
                .Where(x => x.IndexedFile != null
                    && x.IndexedFile.WorkspaceId == workspaceId
                    && !x.IndexedFile.IsDeleted
                    && x.Name == handlerName
                    && (x.Kind == "method" || x.Kind == "function")
                    && EF.Functions.Like(x.IndexedFile.RelativePath, componentStem + "%"))
                .OrderBy(x => x.IndexedFile!.RelativePath.Length)
                .Select(x => new { x.Name, x.Kind, x.IndexedFile!.RelativePath })
                .FirstOrDefaultAsync(cancellationToken);

            if (matchingMethod is null)
            {
                continue;
            }

            var resolvedTarget = $"{matchingMethod.RelativePath}::{matchingMethod.Name}";
            var exists = await db.CodeRelationships.AnyAsync(x =>
                x.WorkspaceId == workspaceId
                && x.SourceIdentifier == relationship.SourceIdentifier
                && x.RelationshipKind == "invokes_handler"
                && x.TargetIdentifier == resolvedTarget,
                cancellationToken);

            if (!exists)
            {
                db.CodeRelationships.Add(CreateRelationship(
                    workspaceId,
                    relationship.SourceKind,
                    relationship.SourceIdentifier,
                    "invokes_handler",
                    matchingMethod.Kind,
                    resolvedTarget,
                    "resolved=true"));
            }
        }
    }

    private static bool TrySplitQualifiedIdentifier(string value, out string path, out string identifier)
    {
        var separatorIndex = value.IndexOf("::", StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            path = string.Empty;
            identifier = value;
            return false;
        }

        path = value[..separatorIndex];
        identifier = value[(separatorIndex + 2)..];
        return true;
    }

    private static string GetComponentStem(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath);
        if (fileName.EndsWith(".component.html", StringComparison.OrdinalIgnoreCase))
        {
            return relativePath[..^".html".Length];
        }

        return Path.Combine(Path.GetDirectoryName(relativePath) ?? string.Empty, Path.GetFileNameWithoutExtension(relativePath));
    }

    private static async Task ResolveCrossStackApiRelationshipsAsync(
        CodeFlowIqDbContext db,
        int workspaceId,
        CancellationToken cancellationToken)
    {
        await db.CodeRelationships
            .Where(x => x.WorkspaceId == workspaceId && x.RelationshipKind == "matches_backend_handler")
            .ExecuteDeleteAsync(cancellationToken);

        var frontendApiCalls = await db.CodeRelationships
            .Where(x => x.WorkspaceId == workspaceId
                && x.RelationshipKind == "calls_api"
                && x.TargetKind == "api")
            .Select(x => new { x.SourceKind, x.SourceIdentifier, Api = x.TargetIdentifier })
            .ToListAsync(cancellationToken);

        var backendHandlers = await db.CodeRelationships
            .Where(x => x.WorkspaceId == workspaceId
                && x.RelationshipKind == "handles_api"
                && x.SourceKind == "symbol"
                && x.TargetKind == "api")
            .Select(x => new { HandlerKind = x.SourceKind, Handler = x.SourceIdentifier, Api = x.TargetIdentifier, x.Metadata })
            .ToListAsync(cancellationToken);

        foreach (var frontendCall in frontendApiCalls)
        {
            if (!TryParseApi(frontendCall.Api, out var frontendMethod, out var frontendRoute))
            {
                continue;
            }

            foreach (var backendHandler in backendHandlers)
            {
                if (!TryParseApi(backendHandler.Api, out var backendMethod, out var backendRoute)
                    || !frontendMethod.Equals(backendMethod, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var matchKind = GetRouteMatchKind(frontendRoute, backendRoute);
                if (matchKind is null)
                {
                    continue;
                }

                db.CodeRelationships.Add(CreateRelationship(
                    workspaceId,
                    frontendCall.SourceKind,
                    frontendCall.SourceIdentifier,
                    "matches_backend_handler",
                    backendHandler.HandlerKind,
                    backendHandler.Handler,
                    $"frontendApi={frontendCall.Api};backendApi={backendHandler.Api};match={matchKind};{backendHandler.Metadata}"));
            }
        }
    }

    private static bool TryParseApi(string value, out string method, out string route)
    {
        var separatorIndex = value.IndexOf(' ');
        if (separatorIndex <= 0 || separatorIndex >= value.Length - 1)
        {
            method = string.Empty;
            route = string.Empty;
            return false;
        }

        method = value[..separatorIndex].Trim();
        route = NormalizeComparableRoute(value[(separatorIndex + 1)..]);
        return true;
    }

    private static string? GetRouteMatchKind(string frontendRoute, string backendRoute)
    {
        if (frontendRoute.Equals(backendRoute, StringComparison.OrdinalIgnoreCase))
        {
            return "exact";
        }

        var frontendSegments = SplitRoute(frontendRoute);
        var backendSegments = SplitRoute(backendRoute);
        if (frontendSegments.Length + 1 == backendSegments.Length && IsVersionSegment(backendSegments[0]))
        {
            backendSegments = backendSegments[1..];
        }
        else if (backendSegments.Length + 1 == frontendSegments.Length && IsVersionSegment(frontendSegments[0]))
        {
            frontendSegments = frontendSegments[1..];
        }

        if (frontendSegments.Length != backendSegments.Length)
        {
            return null;
        }

        for (var i = 0; i < frontendSegments.Length; i++)
        {
            if (frontendSegments[i].Equals(backendSegments[i], StringComparison.OrdinalIgnoreCase)
                || IsRouteParameter(backendSegments[i]))
            {
                continue;
            }

            return null;
        }

        return "template";
    }

    private static string NormalizeComparableRoute(string route)
    {
        var normalized = route.Trim();
        if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(normalized);
            normalized = uri.AbsolutePath;
        }

        return "/" + normalized.Trim('/');
    }

    private static string[] SplitRoute(string route) =>
        route.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsRouteParameter(string segment) =>
        segment.StartsWith('{') && segment.EndsWith('}')
        || segment.StartsWith(':');

    private static bool IsVersionSegment(string segment) =>
        segment.Equals("v1", StringComparison.OrdinalIgnoreCase)
        || segment.Equals("v2", StringComparison.OrdinalIgnoreCase)
        || segment.Equals("v3", StringComparison.OrdinalIgnoreCase)
        || segment.Equals("v4", StringComparison.OrdinalIgnoreCase)
        || segment.StartsWith("v{", StringComparison.OrdinalIgnoreCase);
}
