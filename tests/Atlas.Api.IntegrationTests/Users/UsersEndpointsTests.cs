using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Atlas.Api.IntegrationTests.Infrastructure;
using Atlas.Application.Users.Dtos;
using Atlas.Domain.Enums;
using Xunit;

namespace Atlas.Api.IntegrationTests.Users;

/// <summary>
/// The one thing this project's Application-layer unit tests explicitly could
/// NOT cover (see the class doc comment on Atlas.Application.Tests'
/// UserServiceTests): UsersController's guard against a caller changing their
/// own role or deactivating their own account. That guard reads
/// ICurrentUserService.UserId, which only means something once there is a
/// real authenticated HttpContext behind it — exactly what a
/// WebApplicationFactory-based test, and only that kind of test, can provide.
/// </summary>
[Trait("Category", "Integration")]
[Collection(ApiCollection.Name)]
public class UsersEndpointsTests
{
    private readonly ApiFactory _factory;

    public UsersEndpointsTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ChangeRole_OnYourOwnAccount_Returns400EvenForAnAdmin()
    {
        var client = _factory.CreateClient();
        var admin = await AuthTestHelper.RegisterAsync(client, _factory.SeededOrganizationId, UserRole.Admin);
        client.DefaultRequestHeaders.Authorization = new("Bearer", admin.Token);

        var response = await client.PostAsJsonAsync($"/api/users/{admin.UserId}/role", new ChangeUserRoleRequest(UserRole.Manager));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Deactivate_YourOwnAccount_Returns400EvenForAnAdmin()
    {
        var client = _factory.CreateClient();
        var admin = await AuthTestHelper.RegisterAsync(client, _factory.SeededOrganizationId, UserRole.Admin);
        client.DefaultRequestHeaders.Authorization = new("Bearer", admin.Token);

        var response = await client.PostAsync($"/api/users/{admin.UserId}/deactivate", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangeRole_ByAManagerOnADifferentUser_Succeeds()
    {
        var managerClient = await _factory.CreateAuthenticatedClientAsync(UserRole.Manager);
        var registrationClient = _factory.CreateClient();
        var agent = await AuthTestHelper.RegisterAsync(registrationClient, _factory.SeededOrganizationId, UserRole.Agent);

        var response = await managerClient.PostAsJsonAsync($"/api/users/{agent.UserId}/role", new ChangeUserRoleRequest(UserRole.Manager));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<UserDto>();
        Assert.Equal(UserRole.Manager, updated!.Role);
    }

    /// <summary>Customer has neither User.Read nor User.Manage (see RolePermissions) — [Authorize(Policy=...)] must reject this before UsersController.Search ever runs.</summary>
    [Fact]
    public async Task Search_AsACustomer_Returns403()
    {
        var customerClient = await _factory.CreateAuthenticatedClientAsync(UserRole.Customer);

        var response = await customerClient.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
