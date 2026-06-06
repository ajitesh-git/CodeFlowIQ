using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CodeFlowIQ.Data;

public sealed class DesignTimeCodeFlowIqDbContextFactory : IDesignTimeDbContextFactory<CodeFlowIqDbContext>
{
    public CodeFlowIqDbContext CreateDbContext(string[] args)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "codeflowiq-design-time.db");
        var options = new DbContextOptionsBuilder<CodeFlowIqDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        return new CodeFlowIqDbContext(options);
    }
}
