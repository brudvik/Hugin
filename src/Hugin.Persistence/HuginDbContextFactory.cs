using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hugin.Persistence;

/// <summary>
/// Design-time factory for EF Core migrations.
/// </summary>
public sealed class HuginDbContextFactory : IDesignTimeDbContextFactory<HuginDbContext>
{
    public HuginDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<HuginDbContext>();

        // Default connection string for migrations
        var connectionString = Environment.GetEnvironmentVariable("HUGIN_DB_CONNECTION")
            ?? "Host=localhost;Database=hugin;Username=hugin;Password=hugin";

        optionsBuilder.UseNpgsql(connectionString);

        return new HuginDbContext(optionsBuilder.Options);
    }
}
