using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Atlas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Atlas.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Boots a real Atlas.Api host (real DI container, real JWT authentication,
/// real ASP.NET Core routing/authorization pipeline) against a dedicated
/// AtlasDb_Test LocalDB database — see appsettings.Testing.json for exactly
/// which settings differ from a normal `dotnet run`. UseEnvironment("Testing")
/// below is what makes ASP.NET Core's configuration system pick that file up
/// (the same layering mechanism that makes appsettings.Development.json apply
/// to `dotnet run`) — see Program.cs.
///
/// One instance of this class is shared across every integration test class
/// via ApiCollection/[CollectionDefinition] below (xUnit's own recommended
/// pattern for a fixture that is expensive to build and safe to share): the
/// EnsureDeletedAsync + MigrateAsync in InitializeAsync runs exactly ONCE per
/// `dotnet test` invocation, not once per test class or test case. That is
/// only safe because every test in this project is written to generate its
/// own unique data (a fresh Guid-suffixed email, a fresh organization/ticket
/// title, etc. — the exact same discipline the bash smoke-test scripts in
/// scripts/ already use against Peter's real dev database) rather than
/// assuming an empty table or a fixed row count.
///
/// Deliberately does NOT drop the database again in DisposeAsync: leaving
/// AtlasDb_Test around after a run makes it possible to open it in SSMS/Azure
/// Data Studio afterwards to see exactly what a failing test left behind,
/// the same "prefer being able to look at what actually happened" instinct
/// behind this project's "always verify, never trust a report" rule for
/// pushing files to Peter's machine. The next test RUN's InitializeAsync
/// drops and recreates it anyway, so nothing is lost by skipping the cleanup.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>
    /// A single Organization row, seeded once here so every test class has
    /// somewhere valid to register users against — User.OrganizationId is a
    /// real foreign key (see AtlasDbContext's model configuration), so
    /// POST /api/auth/register against a made-up Guid would fail with a
    /// foreign-key violation before the test's actual assertion ever runs.
    /// Tests that specifically need a *second*, distinct organization (e.g.
    /// proving a permission or filter is organization-scoped) create their
    /// own via POST /api/organizations instead of relying on this one.
    /// </summary>
    public Guid SeededOrganizationId { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }

    // Explicit interface implementations: WebApplicationFactory<TEntryPoint>
    // already implements IAsyncDisposable (ValueTask DisposeAsync()) itself,
    // so a plain "public Task DisposeAsync()" here would collide with it —
    // C# can't overload on return type alone. Implementing IAsyncLifetime
    // explicitly sidesteps that entirely rather than fighting it.
    async Task IAsyncLifetime.InitializeAsync()
    {
        // Touching Services is what actually starts the TestServer/host for
        // the first time (base WebApplicationFactory builds it lazily) — done
        // here, up front, rather than implicitly on whichever test happens to
        // create the first HttpClient.
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AtlasDbContext>();

        // Program.cs's own auto-migrate block is hard-tied to
        // app.Environment.IsDevelopment() (see the comment right above it) and
        // deliberately does NOT run for the Testing environment — so this
        // factory is responsible for its own schema setup, the same way a
        // human running `dotnet ef database update` by hand would be.
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.MigrateAsync();

        var organization = Organization.Create($"Integration Test Org {Guid.NewGuid():N}", OrganizationType.Customer);
        dbContext.Organizations.Add(organization);
        await dbContext.SaveChangesAsync();

        SeededOrganizationId = organization.Id;
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// Wires ApiFactory up as an xUnit collection fixture: every test class
/// carrying [Collection(Name)] shares the exact same ApiFactory instance (and
/// therefore the same running host and the same AtlasDb_Test database) rather
/// than each getting — and each paying the EnsureDeletedAsync/MigrateAsync
/// cost for — its own.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "Api collection";
}
