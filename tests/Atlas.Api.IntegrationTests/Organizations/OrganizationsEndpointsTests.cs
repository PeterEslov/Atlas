using System.Net;
using System.Net.Http.Json;
using Atlas.Api.IntegrationTests.Infrastructure;
using Atlas.Application.Organizations.Dtos;
using Atlas.Domain.Enums;
using Xunit;

namespace Atlas.Api.IntegrationTests.Organizations;

[Trait("Category", "Integration")]
[Collection(ApiCollection.Name)]
public class OrganizationsEndpointsTests
{
    private readonly ApiFactory _factory;

    public OrganizationsEndpointsTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Create_ThenRename_ThenDeactivate_ThenReactivate_AsAdmin_RoundTrips()
    {
        var adminClient = await _factory.CreateAuthenticatedClientAsync(UserRole.Admin);
        var name = $"ACME {Guid.NewGuid():N}";

        var createResponse = await adminClient.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest(name, OrganizationType.Customer));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<OrganizationDetailDto>();
        Assert.Equal(name, created!.Name);
        Assert.True(created.IsActive);

        var renameResponse = await adminClient.PostAsJsonAsync($"/api/organizations/{created.Id}/rename", new RenameOrganizationRequest($"{name} GmbH"));
        Assert.Equal(HttpStatusCode.OK, renameResponse.StatusCode);
        var renamed = await renameResponse.Content.ReadFromJsonAsync<OrganizationDetailDto>();
        Assert.Equal($"{name} GmbH", renamed!.Name);

        var deactivateResponse = await adminClient.PostAsync($"/api/organizations/{created.Id}/deactivate", content: null);
        Assert.Equal(HttpStatusCode.OK, deactivateResponse.StatusCode);
        Assert.False((await deactivateResponse.Content.ReadFromJsonAsync<OrganizationDetailDto>())!.IsActive);

        var reactivateResponse = await adminClient.PostAsync($"/api/organizations/{created.Id}/reactivate", content: null);
        Assert.Equal(HttpStatusCode.OK, reactivateResponse.StatusCode);
        Assert.True((await reactivateResponse.Content.ReadFromJsonAsync<OrganizationDetailDto>())!.IsActive);

        var fetched = await adminClient.GetFromJsonAsync<OrganizationDetailDto>($"/api/organizations/{created.Id}");
        Assert.Equal($"{name} GmbH", fetched!.Name);
    }

    /// <summary>
    /// Organization.Manage is deliberately Admin-only, one notch narrower than
    /// User.Manage/Project.Manage/Team.Manage (all granted to Manager too) —
    /// see RolePermissions' class doc comment for the tenant-boundary
    /// reasoning. A Manager token must get 403 here even though the same
    /// token succeeds against e.g. POST /api/projects.
    /// </summary>
    [Fact]
    public async Task Create_AsAManager_Returns403()
    {
        var managerClient = await _factory.CreateAuthenticatedClientAsync(UserRole.Manager);

        var response = await managerClient.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest($"Should not exist {Guid.NewGuid():N}", OrganizationType.Customer));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithANameThatAlreadyExists_Returns400()
    {
        var adminClient = await _factory.CreateAuthenticatedClientAsync(UserRole.Admin);
        var name = $"Duplicate {Guid.NewGuid():N}";
        await adminClient.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest(name, OrganizationType.Customer));

        var response = await adminClient.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest(name, OrganizationType.Customer));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
