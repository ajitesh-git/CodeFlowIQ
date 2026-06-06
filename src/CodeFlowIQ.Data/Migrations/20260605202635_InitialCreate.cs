using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeFlowIQ.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Workspaces",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RootPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    GitRootPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    CurrentBranch = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    HeadCommitSha = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastIndexedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workspaces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CodeRelationships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkspaceId = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    SourceIdentifier = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    RelationshipKind = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    TargetKind = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    TargetIdentifier = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Metadata = table.Column<string>(type: "TEXT", nullable: true),
                    DiscoveredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeRelationships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CodeRelationships_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IndexedFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkspaceId = table.Column<int>(type: "INTEGER", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    FullPath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    LanguageId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    LastWriteTimeUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IndexedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    ParseStatus = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    ParseError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndexedFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IndexedFiles_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CodeSymbols",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IndexedFileId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    ContainerName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ColumnNumber = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeSymbols", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CodeSymbols_IndexedFiles_IndexedFileId",
                        column: x => x.IndexedFileId,
                        principalTable: "IndexedFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CodeRelationships_RelationshipKind",
                table: "CodeRelationships",
                column: "RelationshipKind");

            migrationBuilder.CreateIndex(
                name: "IX_CodeRelationships_WorkspaceId_SourceKind_SourceIdentifier",
                table: "CodeRelationships",
                columns: new[] { "WorkspaceId", "SourceKind", "SourceIdentifier" });

            migrationBuilder.CreateIndex(
                name: "IX_CodeRelationships_WorkspaceId_TargetKind_TargetIdentifier",
                table: "CodeRelationships",
                columns: new[] { "WorkspaceId", "TargetKind", "TargetIdentifier" });

            migrationBuilder.CreateIndex(
                name: "IX_CodeSymbols_IndexedFileId_Name_Kind",
                table: "CodeSymbols",
                columns: new[] { "IndexedFileId", "Name", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_CodeSymbols_Name",
                table: "CodeSymbols",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_IndexedFiles_WorkspaceId_LanguageId",
                table: "IndexedFiles",
                columns: new[] { "WorkspaceId", "LanguageId" });

            migrationBuilder.CreateIndex(
                name: "IX_IndexedFiles_WorkspaceId_RelativePath",
                table: "IndexedFiles",
                columns: new[] { "WorkspaceId", "RelativePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_RootPath",
                table: "Workspaces",
                column: "RootPath",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CodeRelationships");

            migrationBuilder.DropTable(
                name: "CodeSymbols");

            migrationBuilder.DropTable(
                name: "IndexedFiles");

            migrationBuilder.DropTable(
                name: "Workspaces");
        }
    }
}
