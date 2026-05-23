using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BettingAnalysis.Data;

/// <summary>
/// Design-time factory for EF Core migrations.
/// Enables 'dotnet ef migrations' commands to create DbContext instances.
/// </summary>
public class BettingDbContextFactory : IDesignTimeDbContextFactory<BettingDbContext>
{
    public BettingDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BettingDbContext>();

        // Use LocalDB connection string for design-time operations
        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Database=BettingAnalysisDb;Trusted_Connection=true;TrustServerCertificate=true;");

        return new BettingDbContext(optionsBuilder.Options);
    }
}
