using CodeFlowIQ.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace CodeFlowIQ.Data;

public sealed class CodeFlowIqDbContext(DbContextOptions<CodeFlowIqDbContext> options) : DbContext(options)
{
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<IndexedFile> IndexedFiles => Set<IndexedFile>();
    public DbSet<CodeSymbol> CodeSymbols => Set<CodeSymbol>();
    public DbSet<CodeRelationship> CodeRelationships => Set<CodeRelationship>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Workspace>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.RootPath).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.RootPath).HasMaxLength(1024);
            entity.Property(x => x.GitRootPath).HasMaxLength(1024);
            entity.Property(x => x.CurrentBranch).HasMaxLength(300);
            entity.Property(x => x.HeadCommitSha).HasMaxLength(80);
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(50);
        });

        modelBuilder.Entity<IndexedFile>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.WorkspaceId, x.RelativePath }).IsUnique();
            entity.HasIndex(x => new { x.WorkspaceId, x.LanguageId });
            entity.Property(x => x.RelativePath).HasMaxLength(1024);
            entity.Property(x => x.FullPath).HasMaxLength(2048);
            entity.Property(x => x.LanguageId).HasMaxLength(80);
            entity.Property(x => x.ContentHash).HasMaxLength(128);
            entity.Property(x => x.ParseStatus).HasMaxLength(80);
            entity.HasOne(x => x.Workspace)
                .WithMany()
                .HasForeignKey(x => x.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CodeSymbol>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.IndexedFileId, x.Name, x.Kind });
            entity.HasIndex(x => x.Name);
            entity.Property(x => x.Name).HasMaxLength(500);
            entity.Property(x => x.Kind).HasMaxLength(80);
            entity.Property(x => x.ContainerName).HasMaxLength(500);
            entity.HasOne(x => x.IndexedFile)
                .WithMany(x => x.Symbols)
                .HasForeignKey(x => x.IndexedFileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CodeRelationship>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.WorkspaceId, x.SourceKind, x.SourceIdentifier });
            entity.HasIndex(x => new { x.WorkspaceId, x.TargetKind, x.TargetIdentifier });
            entity.HasIndex(x => x.RelationshipKind);
            entity.Property(x => x.SourceKind).HasMaxLength(80);
            entity.Property(x => x.SourceIdentifier).HasMaxLength(1024);
            entity.Property(x => x.RelationshipKind).HasMaxLength(120);
            entity.Property(x => x.TargetKind).HasMaxLength(80);
            entity.Property(x => x.TargetIdentifier).HasMaxLength(1024);
            entity.HasOne(x => x.Workspace)
                .WithMany()
                .HasForeignKey(x => x.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
