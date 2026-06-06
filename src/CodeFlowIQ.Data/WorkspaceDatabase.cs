using Microsoft.EntityFrameworkCore;

namespace CodeFlowIQ.Data;

public static class WorkspaceDatabase
{
    public const string DirectoryName = ".codeflowiq";
    public const string DatabaseFileName = "codeflowiq.db";

    public static string GetDatabasePath(string workspacePath)
    {
        var root = Path.GetFullPath(workspacePath);
        return Path.Combine(root, DirectoryName, DatabaseFileName);
    }

    public static CodeFlowIqDbContext CreateDbContext(string workspacePath)
    {
        var databasePath = GetDatabasePath(workspacePath);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        var options = new DbContextOptionsBuilder<CodeFlowIqDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        return new CodeFlowIqDbContext(options);
    }

    public static async Task<CodeFlowIqDbContext> OpenMigratedAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var db = CreateDbContext(workspacePath);
        await db.Database.MigrateAsync(cancellationToken);
        return db;
    }
}
