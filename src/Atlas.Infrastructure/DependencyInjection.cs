using Atlas.Application.Common.Interfaces;
using Atlas.Infrastructure.Caching;
using Atlas.Infrastructure.Messaging;
using Atlas.Infrastructure.Persistence;
using Atlas.Infrastructure.Repositories;
using Atlas.Infrastructure.Security;
using Atlas.Infrastructure.Storage;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Infrastructure;

/// <summary>
/// Composition root for the infrastructure layer. Called once from Atlas.Api's
/// Program.cs — nothing outside this file should new up a DbContext or repository.
///
/// Split into five composable pieces (Del 11 split off three; Del 12 added a
/// fourth; Del 13 added this one) rather than one monolithic
/// AddInfrastructure: Atlas.Api needs all five (persistence, blob storage,
/// auth, messaging, caching), but Atlas.Worker — a second host process that
/// reads tickets, writes notifications, and consumes Service Bus events —
/// needs only AddPersistence and AddMessaging, never Blob Storage, JWT auth,
/// or the Redis cache (it never serves GET /api/tickets/stats, since it
/// doesn't even reference ITicketService/TicketService at all — see
/// AddCaching's own doc comment). Before the Del 11 split, giving the worker
/// anything at all meant calling the full AddInfrastructure(), which would
/// have forced it to also carry configuration (and crash at startup without
/// it, per the fail-fast checks below) for systems it never touches.
/// AddInfrastructure itself is unchanged from Atlas.Api's point of view — it
/// still wires up everything, in the same order, so Program.cs there needed
/// no changes at all when Del 12 added AddMessaging, or now when Del 13 adds
/// AddCaching, to the list.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPersistence(configuration);
        services.AddBlobStorage(configuration);
        services.AddAuthInfrastructure(configuration);
        services.AddMessaging(configuration);
        services.AddCaching(configuration);

        return services;
    }

    /// <summary>
    /// The DbContext, the unit of work, and every repository. This is the one
    /// piece every host (Atlas.Api, Atlas.Worker) needs, since both ultimately
    /// just read and write rows in AtlasDb.
    /// </summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
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

        // Notifications (Del 11) — the first thing to actually read/write the
        // Notifications table, which has existed in the schema since Fas 1.
        services.AddScoped<INotificationRepository, NotificationRepository>();

        return services;
    }

    /// <summary>
    /// Azure Blob Storage for ticket attachments (Del 10). Only Atlas.Api needs
    /// this — nothing else in the solution uploads or downloads a file.
    /// </summary>
    public static IServiceCollection AddBlobStorage(this IServiceCollection services, IConfiguration configuration)
    {
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
        // run after the container already exists. PublicAccessType.None means
        // the container itself grants no anonymous access — every download still
        // goes through TicketsController, which enforces Permissions.TicketRead.
        blobContainerClient.CreateIfNotExists(PublicAccessType.None);

        services.AddSingleton(blobContainerClient);
        services.AddScoped<IBlobStorageService, AzureBlobStorageService>();

        return services;
    }

    /// <summary>
    /// JWT issuance and password hashing (Del 5). Only Atlas.Api authenticates
    /// anyone — Atlas.Worker never issues or validates a token.
    /// </summary>
    public static IServiceCollection AddAuthInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();

        return services;
    }

    /// <summary>
    /// Azure Service Bus (Del 12) — publishing from Atlas.Api and, once
    /// TicketAssignedConsumer is registered, consuming from Atlas.Worker.
    /// Both hosts call this, unlike AddBlobStorage/AddAuthInfrastructure
    /// above, which only Atlas.Api needs.
    ///
    /// Unlike every other external dependency in this project (SQL, Blob
    /// Storage), there is no local-emulator branch here at all: Del 10 and
    /// Del 11 each got a free local emulator (LocalDB, Azurite) that speaks
    /// the real wire protocol, but Service Bus has no first-party local
    /// emulator, so — per the explicit choice made for Del 12 — local
    /// development talks to the *same* real Azure namespace production will
    /// use, rather than an in-process fake queue that would only prove the
    /// code compiles, not that publish/consume actually works.
    ///
    /// That means DefaultAzureCredential gets exercised locally for the
    /// first time in this project: SQL uses a connection string, Blob
    /// Storage uses Azurite's fixed well-known key locally, and Key Vault
    /// isn't touched by Atlas.Worker at all — so this is the first piece of
    /// infrastructure where "run it on your own machine" requires an actual
    /// Azure AD identity. Locally, DefaultAzureCredential falls back through
    /// its credential chain to whatever `az login` already set up on the
    /// developer's machine; in Azure, it will instead pick up Atlas.Api's
    /// system-assigned managed identity, the same pattern Blob Storage
    /// already uses. Either identity still needs an explicit RBAC role
    /// assignment on the namespace/topic before any send or receive call
    /// will succeed — "Azure Service Bus Data Sender" for Atlas.Api,
    /// "Data Receiver" (or "Data Owner", covering both) for Atlas.Worker —
    /// see docs/AZURE_DEPLOYMENT.md section 9. Authentication succeeding is
    /// not the same as authorization succeeding; a missing role assignment
    /// surfaces as an Unauthorized error from the SDK at send/receive time,
    /// not at startup.
    ///
    /// The ServiceBusClient itself is a singleton (like BlobContainerClient
    /// above) — it manages its own AMQP connection pool internally and is
    /// explicitly documented by the SDK as safe, and intended, to be shared
    /// for the lifetime of the app rather than constructed per request.
    /// </summary>
    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var fullyQualifiedNamespace = configuration["ServiceBus:FullyQualifiedNamespace"]
            ?? throw new InvalidOperationException(
                "Configuration value 'ServiceBus:FullyQualifiedNamespace' was not found. Set it in " +
                "appsettings.Development.json to 'nspl-sb-core-dev-sc.servicebus.windows.net' (see " +
                "docs/AZURE_DEPLOYMENT.md section 9) — the same value is used in Azure, since Del 12 " +
                "deliberately has no separate local-emulator path (see the class doc comment above).");

        // ServiceBus:ExcludeManagedIdentityCredential — set to true only in
        // appsettings.Development.json, absent (so defaults to false) in
        // appsettings.json — the same "a plain config key decides the
        // environment-specific behaviour" idiom AddBlobStorage above uses
        // (ConnectionString present vs. AccountUrl present), rather than
        // branching on an environment name.
        //
        // Why this is needed: DefaultAzureCredential tries a fixed chain of
        // credential sources in order, and only moves on to the next one
        // when a source throws CredentialUnavailableException — its way of
        // saying "I don't apply here, keep going". Locally, there IS no
        // managed identity, so in principle ManagedIdentityCredential should
        // say exactly that and let the chain fall through to
        // AzureCliCredential (which is what actually authenticates as you,
        // via `az login`). In practice, the SDK's managed-identity probe
        // tries to reach the Instance Metadata Service at 169.254.169.254 —
        // an address that only exists inside an actual Azure VM/App
        // Service — and on a machine where that address is simply
        // unreachable (not "refused", genuinely unreachable), the probe
        // exhausts its retries and throws AuthenticationFailedException
        // instead of CredentialUnavailableException. DefaultAzureCredential
        // treats that as a hard failure of the whole chain, not a "try the
        // next source" signal — so AzureCliCredential never even gets a
        // turn, and every local run fails with exactly the
        // "ManagedIdentityCredential authentication failed: ... 169.254.169.254
        // ..." error this project hit. Excluding ManagedIdentityCredential
        // outright when running locally (where it could never succeed
        // anyway) sidesteps the slow, doomed probe entirely. In Azure,
        // where Atlas.Api's system-assigned managed identity is the whole
        // point, the flag is absent and it stays in the chain.
        var excludeManagedIdentity = configuration.GetValue<bool>("ServiceBus:ExcludeManagedIdentityCredential");
        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ExcludeManagedIdentityCredential = excludeManagedIdentity,
        });

        var serviceBusClient = new ServiceBusClient(fullyQualifiedNamespace, credential);

        services.AddSingleton(serviceBusClient);
        services.AddScoped<ITicketEventPublisher, ServiceBusTicketEventPublisher>();

        return services;
    }

    /// <summary>
    /// Redis-backed cache-aside for GET /api/tickets/stats (Del 13). Only
    /// Atlas.Api needs this — the same "only the host that actually has the
    /// feature gets the registration" rule AddBlobStorage/AddAuthInfrastructure
    /// already follow above: ITicketStatsCache is only ever injected into
    /// TicketService, and TicketService itself is only registered in
    /// Atlas.Api's Program.cs (Atlas.Worker's own Program.cs never references
    /// it — it only needs OverdueTicketNotificationService and
    /// TicketAssignedConsumer).
    ///
    /// Unlike Del 12's Service Bus — no first-party local emulator, so local
    /// development there deliberately talks to the same real Azure namespace
    /// production uses — Redis has a genuine local option: a plain
    /// "docker run -p 6379:6379 redis" container speaks the exact same RESP
    /// wire protocol a real Redis server does. So, like LocalDB (SQL) and
    /// Azurite (Blob Storage) before it, local development here runs against
    /// something real, not a fake and not production itself. That's why
    /// Redis:ConnectionString is a single config key that means
    /// "localhost:6379, no auth" in appsettings.Development.json and "the
    /// real Azure Cache for Redis instance, with its access key baked into
    /// the connection string" in Azure — the same one-key-does-both-
    /// environments idiom as BlobStorage:ConnectionString locally vs.
    /// AccountUrl+managed identity in Azure, except there's no managed-
    /// identity branch to add here: classic (non-Enterprise) Azure Cache for
    /// Redis only supports key-based auth, so unlike Blob Storage and Service
    /// Bus, there's no DefaultAzureCredential path available at all yet. See
    /// the "Known simplifications" note in docs/ARCHITECTURE.md — a
    /// deliberate, documented gap (a Del 20 candidate alongside the other
    /// Key-Vault/RBAC generalization work), not an oversight.
    ///
    /// AddStackExchangeRedisCache registers IDistributedCache, backed by a
    /// StackExchange.Redis connection it manages internally (its own
    /// connection pooling/reconnect logic — the same idea as ServiceBusClient
    /// or BlobContainerClient above being registered once and shared for the
    /// app's lifetime). Nothing in this project talks to StackExchange.Redis's
    /// own client types directly — RedisTicketStatsCache only ever sees the
    /// IDistributedCache abstraction, which is all a single get/set/remove-by-
    /// key cache-aside store needs (see its own doc comment for why that's
    /// enough, and why a Redis outage must never turn into a failed request).
    /// </summary>
    public static IServiceCollection AddCaching(this IServiceCollection services, IConfiguration configuration)
    {
        var redisConnectionString = configuration["Redis:ConnectionString"]
            ?? throw new InvalidOperationException(
                "Configuration value 'Redis:ConnectionString' was not found. Set it in " +
                "appsettings.Development.json to 'localhost:6379' (a local Redis container started " +
                "with 'docker run -p 6379:6379 redis' — see README.md 'Kom igång') or, in Azure, to " +
                "the Azure Cache for Redis connection string (see docs/AZURE_DEPLOYMENT.md section 10).");

        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnectionString;
            options.InstanceName = "atlas:";
        });

        services.AddScoped<ITicketStatsCache, RedisTicketStatsCache>();

        return services;
    }
}
