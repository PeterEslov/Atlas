using System.Text;
using Atlas.Api.Middleware;
using Atlas.Api.OpenApi;
using Atlas.Api.Services;
using Atlas.Application.Audit.Services;
using Atlas.Application.Auth.Services;
using Atlas.Application.Common.Interfaces;
using Atlas.Application.Organizations.Services;
using Atlas.Application.Projects.Services;
using Atlas.Application.Teams.Services;
using Atlas.Application.Tickets.Services;
using Atlas.Application.Users.Services;
using Atlas.Domain.Security;
using Atlas.Infrastructure;
using Atlas.Infrastructure.Persistence;
using Atlas.Infrastructure.Security;
using Azure.Identity;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Serilog;

// ---- Structured logging bootstrap (Del 14) ---------------------------------
//
// Two-stage Serilog initialization — the pattern Serilog's own ASP.NET Core
// integration recommends, not something invented here. A minimal "bootstrap"
// logger exists from this very first statement, writing only to the console,
// so that a failure during startup itself (bad configuration, a dependency
// that can't be resolved) is still logged somewhere instead of vanishing if
// the process dies before builder.Host.UseSerilog below — which needs
// configuration and DI that don't exist yet at this point — ever runs.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Atlas.Api");

    var builder = WebApplication.CreateBuilder(args);

    // Replaces the default Microsoft.Extensions.Logging console provider
    // wholesale — every existing _logger.LogInformation/LogWarning call
    // already in this codebase (AuthService, TicketService, UserService,
    // OrganizationService, ProjectService, ...) is completely untouched by
    // this change. They're written against ILogger<T>, an abstraction
    // Serilog implements as a provider exactly the way the built-in console
    // provider did. What changes is what happens to those calls afterwards:
    // the named properties in each message template (the {TicketId} in
    // "Ticket {TicketId} created by user {UserId}") now survive as real,
    // structured fields attached to the log event, not just interpolated
    // into one formatted string — in the console sink below via Serilog's
    // own rendering, and in Application Insights (once
    // ApplicationInsights:ConnectionString is set) as custom properties on a
    // Trace telemetry item, queryable there by TicketId rather than by
    // grepping text.
    builder.Host.UseSerilog((context, _, loggerConfiguration) =>
    {
        loggerConfiguration
            // MinimumLevel/Override come from the "Serilog" config section
            // (appsettings.json / appsettings.Development.json) — the direct
            // replacement for the old "Logging:LogLevel" section, which
            // Serilog never reads. Which sinks exist, on the other hand, is
            // an architectural decision made here in code, not something
            // this project wants toggleable from JSON — see the
            // ApplicationInsights block below for why.
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "Atlas.Api")
            .WriteTo.Console();

        // ApplicationInsights:ConnectionString is a plain App Setting in
        // Azure, not a Key Vault secret — the same "this identifies a
        // resource, it isn't a credential" reasoning BlobStorage:AccountUrl
        // already established (Del 10): it tells the SDK which Application
        // Insights resource to send telemetry TO, and by itself grants only
        // "send telemetry", never "read it back". It's empty in
        // appsettings.Development.json, so a local `dotnet run` only ever
        // logs to the console sink above — deliberately, not a gap: unlike
        // Redis (a Docker container) or Azure SQL (LocalDB), Application
        // Insights has no free local emulator, and routing every local
        // request to a real cloud resource by default — the way Del 12
        // accepted doing for Service Bus, which has the same no-emulator
        // problem — isn't worth it just for logs already readable on the
        // console in front of you. See docs/ARCHITECTURE.md's Del 14 section
        // for the full reasoning.
        var appInsightsConnectionString = context.Configuration["ApplicationInsights:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
        {
            var telemetryConfiguration = new TelemetryConfiguration
            {
                ConnectionString = appInsightsConnectionString
            };
            loggerConfiguration.WriteTo.ApplicationInsights(telemetryConfiguration, TelemetryConverter.Traces);
        }
    });

    // ---- Key Vault (pulled forward from Del 20) ----------------------------
    //
    // Originally planned as its own later Del, but Peter's subscription already
    // has a Key Vault sitting there unused — building the "plain-text App Service
    // setting" version of the JWT signing key first, only to redo it as a Key
    // Vault secret once Del 20 arrived, would just be throwaway work. So the
    // signing key moves to Key Vault as part of Del 8 instead; the Azure SQL
    // connection string will join it the same way once Del 9 exists. See
    // docs/AZURE_DEPLOYMENT.md for how the secret gets there and how App Service
    // authenticates to read it (a system-assigned managed identity — no
    // connection string or key needed for Key Vault access itself).
    //
    // KeyVault:Name is only ever set as an Azure Application Setting (see the
    // deployment doc) — never in an appsettings.*.json file — so this block is a
    // no-op for local `dotnet run`, which keeps using the plain-text signing key
    // in appsettings.Development.json exactly as before.
    var keyVaultName = builder.Configuration["KeyVault:Name"];
    if (!string.IsNullOrWhiteSpace(keyVaultName))
    {
        builder.Configuration.AddAzureKeyVault(
            new Uri($"https://{keyVaultName}.vault.azure.net/"),
            new DefaultAzureCredential());
    }

    // ---- Services -----------------------------------------------------------

    builder.Services.AddControllers();

    // Generates /openapi/v1.json using ASP.NET Core's built-in OpenAPI support —
    // no Swashbuckle SwaggerGen involved. UseSwaggerUI below just needs that URL.
    builder.Services.AddOpenApi("v1", options =>
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = "Project Atlas API";
            document.Info.Version = "v1";
            document.Info.Description = "Ticket & operations management platform for Northstar IT.";
            return Task.CompletedTask;
        });

        // Adds the "Bearer" security scheme so Swagger UI shows an Authorize
        // button (Del 5) — see BearerSecuritySchemeTransformer for details.
        options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();

        // Marks every non-anonymous operation as requiring that scheme — without
        // this, Swagger UI's Authorize button exists but never actually attaches
        // the token to requests. See AuthorizedOperationTransformer for the full
        // story (confirmed live via the browser Network tab on 2026-09-09).
        options.AddOperationTransformer<AuthorizedOperationTransformer>();
    });

    builder.Services.AddInfrastructure(builder.Configuration);

    // Unauthenticated liveness probe for Azure App Service's "Health check" feature
    // (Del 8): App Service pings this path on every instance and takes one out of
    // rotation if it stops responding. Deliberately NOT wired to the database —
    // Azure can't reach the local dev SQL Server used until Del 9 lands Azure SQL,
    // and a health check that depends on something not guaranteed to work yet
    // would just make App Service think every instance is permanently unhealthy.
    // A DbContext-backed check (AddDbContextCheck<AtlasDbContext>()) is the
    // natural follow-up once there's a real cloud database behind it.
    builder.Services.AddHealthChecks();

    // Azure App Service terminates TLS at its own edge/reverse proxy and forwards
    // the request to this app as plain HTTP on an internal port. Without this,
    // the app thinks every request arrived over HTTP, and app.UseHttpsRedirection()
    // below would keep "helpfully" redirecting an already-HTTPS client back to
    // http://, which App Service then upgrades again — an infinite redirect loop
    // in the browser. Trusting the platform's X-Forwarded-Proto/-For headers here
    // (with KnownNetworks/KnownProxies cleared, since App Service's proxy isn't a
    // fixed IP this app can pin down in config) is the standard fix; it's a no-op
    // for local `dotnet run`, where there's no reverse proxy in front at all.
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
    builder.Services.AddScoped<ITicketService, TicketService>();
    builder.Services.AddScoped<IAuthService, AuthService>();
    builder.Services.AddScoped<IOrganizationService, OrganizationService>();
    builder.Services.AddScoped<IUserService, UserService>();
    builder.Services.AddScoped<IProjectService, ProjectService>();
    builder.Services.AddScoped<ITeamService, TeamService>();
    builder.Services.AddScoped<IAuditLogService, AuditLogService>();

    builder.Services.AddCors(options =>
    {
        // Permissive policy for local development only (e.g. a future React dev
        // server on http://localhost:5173, see Del 21). Tightened before any real
        // deployment — see Del 8/19 for the Azure App Service configuration.
        options.AddPolicy("AllowLocalDev", policy =>
            policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
                  .AllowAnyHeader()
                  .AllowAnyMethod());
    });

    // ---- Authentication & authorization (Del 5) ------------------------------
    //
    // JwtSettings is bound eagerly here (not just via IOptions in DI) because the
    // signing key has to be turned into a SymmetricSecurityKey right now, while
    // building TokenValidationParameters. AddInfrastructure above also registers
    // IOptions<JwtSettings> for JwtTokenGenerator's own use when issuing tokens.
    var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
        ?? throw new InvalidOperationException(
            "Configuration section 'Jwt' was not found. Set it in appsettings.Development.json " +
            "(local signing key) or, in Azure, as a Key Vault secret (see the Key Vault block above).");

    if (string.IsNullOrWhiteSpace(jwtSettings.SigningKey) || Encoding.UTF8.GetByteCount(jwtSettings.SigningKey) < 32)
    {
        throw new InvalidOperationException(
            "Jwt:SigningKey is missing or too short (must be at least 32 bytes / 256 bits for HS256). " +
            "Set a real value in appsettings.Development.json for local dev, or as the Key Vault secret " +
            "Jwt--SigningKey for Azure.");
    }

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            // Without this, the JWT handler silently remaps well-known claim types
            // (e.g. "sub" -> ClaimTypes.NameIdentifier) using its default inbound
            // claim map, which would break every FindFirst("sub")/("email") lookup
            // in CurrentUserService and AuthController against the raw claim types
            // JwtTokenGenerator actually issues.
            options.MapInboundClaims = false;

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtSettings.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SigningKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            };
        });

    builder.Services.AddAuthorization(options =>
    {
        // One policy per permission, each satisfied by a matching "permission"
        // claim (see RolePermissions / JwtTokenGenerator). Controllers apply
        // these with [Authorize(Policy = Permissions.TicketCreate)] etc. rather
        // than the coarser built-in role-based [Authorize(Roles = ...)].
        foreach (var permission in Permissions.All)
        {
            options.AddPolicy(permission, policy => policy.RequireClaim("permission", permission));
        }
    });

    var app = builder.Build();

    // ---- Middleware pipeline ------------------------------------------------

    // Must run before UseHttpsRedirection (and ideally as early as possible) so
    // every later stage sees the request's real scheme/remote IP rather than
    // what App Service's internal proxy hop looks like. See the comment on
    // AddForwardedHeaders above for why this is here at all.
    app.UseForwardedHeaders();

    // Del 14: logs one structured line per HTTP request — "HTTP {RequestMethod}
    // {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms" — through the
    // same Serilog pipeline configured above. This is the one thing none of the
    // existing per-operation _logger calls in the services give you: a record of
    // every single request, including the ones that never reach a service method
    // at all (a 401 from a missing token, a 403 from a failed policy check, a 404
    // for an unknown route). Placed immediately after UseForwardedHeaders and
    // before the exception handler so it wraps the entire rest of the pipeline —
    // the status code it reports is the one the client actually received, even
    // when UseAtlasExceptionHandling below is what produced it.
    app.UseSerilogRequestLogging();

    app.UseAtlasExceptionHandling();

    // Swagger UI's availability is now a config switch (EnableSwaggerUi), not an
    // environment check — see appsettings.Development.json (true) vs.
    // appsettings.json's default (false). That split matters once Del 8 deploys
    // to Azure: flipping this on there for portfolio/demo viewing must not also
    // flip on the auto-migrate-on-startup block below, which stays hard-tied to
    // app.Environment.IsDevelopment() precisely so a config change alone can
    // never trigger it against a real database.
    if (app.Configuration.GetValue<bool>("EnableSwaggerUi"))
    {
        app.MapOpenApi();
        app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Project Atlas API v1"));
    }

    // Convenience only: applies pending EF Core migrations automatically on
    // startup so a fresh clone works with just `dotnet run` — or, since
    // Del 17, `docker compose up`. Extended to the "Docker" environment
    // (docker-compose.yml sets ASPNETCORE_ENVIRONMENT=Docker) alongside
    // "Development" because they carry exactly the same trust level: both
    // point at a fresh, disposable, local-only SQL Server (LocalDB or
    // docker-compose's own sqlserver container) with nothing worth
    // protecting, never a stand-in for a real cloud database. Never do this
    // against Azure SQL in production — deploy migrations explicitly
    // (Del 8/18) — because neither environment name is ever what a real
    // Azure App Service deployment runs under.
    if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Docker"))
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AtlasDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    app.UseHttpsRedirection();
    app.UseCors("AllowLocalDev");
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();

    // Unauthenticated on purpose — see the AddHealthChecks() comment above.
    app.MapHealthChecks("/health");

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    // HostAbortedException is deliberately excluded here, not missed: it's the
    // exact, documented exception `dotnet ef migrations add`/`dotnet ef database
    // update` throw on purpose once they've built just enough of the host to
    // discover AtlasDbContext (Atlas.Api's own Microsoft.EntityFrameworkCore.Design
    // reference exists for precisely this design-time build) — it's how EF's
    // tooling stops the app short of ever reaching app.Run(). Without this
    // filter, every single `dotnet ef` command in this repo's own "Getting
    // started" instructions (README.md) would print a scary "Atlas.Api
    // terminated unexpectedly" Fatal log line despite doing exactly what it was
    // supposed to do — a well-known Serilog + EF Core interaction, not something
    // specific to this project (see docs/ARCHITECTURE.md's Del 14 section).
    Log.Fatal(ex, "Atlas.Api terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}

// Exposed for WebApplicationFactory<Program> in integration tests (Del 16).
public partial class Program
{
}
