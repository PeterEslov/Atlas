using System.Net;
using System.Net.Http.Json;
using Atlas.Api.IntegrationTests.Infrastructure;
using Atlas.Application.Auth.Dtos;
using Atlas.Domain.Enums;
using Xunit;

namespace Atlas.Api.IntegrationTests.Auth;

[Trait("Category", "Integration")]
[Collection(ApiCollection.Name)]
public class AuthEndpointsTests
{
    private readonly ApiFactory _factory;

    public AuthEndpointsTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_WithAValidRequest_Returns201WithATokenAndTheGrantedPermissions()
    {
        var client = _factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@integration.test";

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("Anna Agent", email, AuthTestHelper.Password, _factory.SeededOrganizationId, UserRole.Agent));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.Token));
        Assert.Equal(UserRole.Agent, body.Role);
        // Agent = Customer's two permissions + Update/Assign — see RolePermissions.
        Assert.Contains("Ticket.Assign", body.Permissions);
        Assert.DoesNotContain("Organization.Manage", body.Permissions);
    }

    /// <summary>
    /// Registering with an email that already exists actually comes back as
    /// 401, not the 400 the [ProducesResponseType(StatusCodes.Status400BadRequest)]
    /// attribute on AuthController.Register documents: AuthService.RegisterAsync
    /// throws AuthenticationException for a duplicate email (see its doc
    /// comment — the same exception type it uses for "wrong password"), and
    /// ExceptionHandlingMiddleware maps every AuthenticationException to 401,
    /// never 400. This is exactly the kind of gap only an HTTP-level test can
    /// catch — a service-layer unit test asserting
    /// ThrowsAsync&lt;AuthenticationException&gt; (see AuthServiceTests) never
    /// looks at the status code the middleware actually produces from it.
    /// </summary>
    [Fact]
    public async Task Register_WithAnEmailThatAlreadyExists_Returns401NotTheDocumented400()
    {
        var client = _factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@integration.test";
        await AuthTestHelper.RegisterAsync(client, _factory.SeededOrganizationId, UserRole.Customer, email);

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("Someone Else", email, AuthTestHelper.Password, _factory.SeededOrganizationId, UserRole.Customer));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithTheCorrectPassword_Returns200WithAToken()
    {
        var client = _factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@integration.test";
        await AuthTestHelper.RegisterAsync(client, _factory.SeededOrganizationId, UserRole.Manager, email);

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, AuthTestHelper.Password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.Equal(UserRole.Manager, body!.Role);
    }

    [Fact]
    public async Task Login_WithTheWrongPassword_Returns401WithoutRevealingWhichFieldWasWrong()
    {
        var client = _factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@integration.test";
        await AuthTestHelper.RegisterAsync(client, _factory.SeededOrganizationId, UserRole.Customer, email);

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "TheWrongPassword1!"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GettingAProtectedEndpoint_WithNoBearerToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/tickets");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// featurelogin branch: GET /api/auth/organizations powers the Register
    /// form's dropdown, so — unlike /api/tickets above — it has to stay
    /// reachable with no bearer token at all. Confirms both that it isn't
    /// caught by the same authorization requirement every other controller
    /// has, and that it actually returns the organization ApiFactory seeds
    /// for every integration test (see AuthTestHelper.RegisterAsync's own
    /// use of SeededOrganizationId above).
    /// </summary>
    [Fact]
    public async Task GetRegistrableOrganizations_WithNoBearerToken_Returns200WithTheSeededOrganization()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/auth/organizations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<OrganizationOptionDto>>();
        Assert.NotNull(body);
        Assert.Contains(body!, o => o.Id == _factory.SeededOrganizationId);
    }
}
