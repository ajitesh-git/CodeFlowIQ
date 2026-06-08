using CodeFlowIQ.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace CodeFlowIQ.Indexing;

internal static class WorkspaceQueryFilters
{
    public static IQueryable<CodeRelationship> ApplyRelationshipFilters(
        IQueryable<CodeRelationship> query,
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

    public static IQueryable<CodeRelationship> ExcludeTestRelationships(IQueryable<CodeRelationship> query) =>
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

    public static IQueryable<IndexedFile> ExcludeTestFiles(IQueryable<IndexedFile> query) =>
        query.Where(x =>
            !EF.Functions.Like(x.RelativePath, "%\\Tests\\%")
            && !EF.Functions.Like(x.RelativePath, "%/Tests/%")
            && !EF.Functions.Like(x.RelativePath, "%.Tests\\%")
            && !EF.Functions.Like(x.RelativePath, "%.Tests/%")
            && !EF.Functions.Like(x.RelativePath, "%\\Test\\%")
            && !EF.Functions.Like(x.RelativePath, "%/Test/%"));
}
