using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Atlas.Infrastructure.Persistence;

/// <summary>
/// Lets the `dotnet ef` CLI create a DbContext at design time (for migrations)
/// without spinning up the full Atlas.Api host. Reads the same appsettings.json /
/// appsettings.Development.json the API uses, from Atlas.Api's directory, so both
/// paths resolve the same connection string.
///
/// Usage (run from src/Atlas.Api):
///   dotnet ef migrations add InitialCreate --project ../Atlas.Infrastructure --startup-project .
///   dotnet ef database update --project ../Atlas.Infrastructure --startup-project .
/// </summary>
public sealed class AtlasDbContextFactory : IDesignTimeDbContextFactory<AtlasDbContext>
{
    public AtlasDbContext CreateDbContext(string[] args)
    {
        var basePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "Atlas.Api");
        if (!Directory.Exists(basePath))
        {
            basePath = Directory.GetCurrentDirectory();
        }

        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("AtlasDb")
            ?? "Server=(localdb)\\mssqllocaldb;Database=AtlasDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

        var optionsBuilder = new DbContextOptionsBuilder<AtlasDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new AtlasDbContext(optionsBuilder.Options);
    }
}
