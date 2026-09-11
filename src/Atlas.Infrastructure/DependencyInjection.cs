using Atlas.Application.Common.Interfaces;
using Atlas.Infrastructure.Persistence;
using Atlas.Infrastructure.Repositories;
using Atlas.Infrastructure.Security;
using Atlas.Infrastructure.Storage;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Infrastructure;

/// <summary>
/// Composition root for the infrastructure layer. Called once from Atlas.Api's
/// Program.cs — nothing outside this file should new up a DbContext or repository.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AtlasDb")
            ?? throw new InvalidOperationException(
                "Connection string 'AtlasDb' was not found. Set it in appsettings.Development.json " +
                "(local SQL Server / LocalDB) or, in Azure, as the Key Vault secret " +
                "ConnectionStrings--AtlasDb (Del 9 — see docs/AZURE_DEPLOYMENT.md section 7; the Key " +
                "Vault wiring itself was set up back in Del 8, see Program.cs).");

        services.AddDbContext<AtlasDbContext>(options =>
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
                sqlOptions.CommandTimeout(30);
            }));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AtlasDbContext>());
        services.AddScoped<ITicketRepository, TicketRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<ITeamRepository, TeamRepository>();

        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();

        // ---- Blob Storage (Del 10) --------------------------------------------
        //
        // Same dual-path shape as the AtlasDb connection string above, for the
        // same reason: BlobStorage:ConnectionString means local development
        // against Azurite (a free, Microsoft-official emulator that speaks the
        // real Blob Storage wire protocol — see docs/AZURE_DEPLOYMENT.md section 8
        // and README "Kom igång"), while BlobStorage:AccountUrl means Azure,
        // authenticated via DefaultAzureCredential against the App Service's
        // system-assigned managed identity (granted the "Storage Blob Data
        // Contributor" RBAC role on the storage account) — no account key or
        // connection string stored anywhere for the cloud path, matching Key
        // Vault's and Azure SQL's managed-identity/RBAC approach.
        //
        // Unlike the SQL connection string and the JWT signing key, the account
        // URL by itself isn't a secret — knowing it grants nothing without an
        // Azure AD identity Azure also trusts — so it's a plain Azure App
        // Setting rather than a Key Vault entry, the same way KeyVault:Name
        // itself is (see Program.cs).
        var blobConnectionString = configuration["BlobStorage:ConnectionString"];
        var blobContainerName = configuration["BlobStorage:ContainerName"];
        if (string.IsNullOrWhiteSpace(blobContainerName))
        {
            blobContainerName = "attachments";
        }

        BlobContainerClient blobContainerClient;
        if (!string.IsNullOrWhiteSpace(blobConnectionString))
        {
            blobContainerClient = new BlobContainerClient(blobConnectionString, blobContainerName);
        }
        else
        {
            var blobAccountUrl = configuration["BlobStorage:AccountUrl"]
                ?? throw new InvalidOperationException(
                    "Neither BlobStorage:ConnectionString (local Azurite) nor BlobStorage:AccountUrl " +
                    "(Azure, via managed identity) was found. Set the former in " +
                    "appsettings.Development.json, or the latter as an Azure App Setting " +
                    "(see docs/AZURE_DEPLOYMENT.md section 8).");

            blobContainerClient = new BlobContainerClient(
                new Uri($"{blobAccountUrl.TrimEnd('/')}/{blobContainerName}"),
                new DefaultAzureCredential());
        }

        // Idempotent and synchronous on purpose: this runs once, here, during
        // startup composition (before the app accepts any requests), so there's
        // no async context to await into and no harm in it being a no-op on every
        // run after the container already exists. PublicAccessType.None means the
        // container itself grants no anonymous access — every download still goes
        // through TicketsController, which enforces Permissions.TicketRead.
        blobContainerClient.CreateIfNotExists(PublicAccessType.None);

        services.AddSingleton(blobContainerClient);
        services.AddScoped<IBlobStorageService, AzureBlobStorageService>();

        return services;
    }
}
