using System.Net;
using System.Net.Http.Json;
using Atlas.Api.IntegrationTests.Infrastructure;
using Atlas.Application.Common.Models;
using Atlas.Application.Organizations.Dtos;
using Atlas.Application.Audit.Dtos;
using Atlas.Domain.Enums;
using Xunit;

namespace Atlas.Api.IntegrationTests.Audit;

[Trait("Category", "Integration")]
[Collection(ApiCollection.Name)]
public class AuditLogEndpointsTests
{
    private readonly ApiFactory _factory;

    public AuditLogEndpointsTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Search_AfterCreatingAnOrganization_AsAdmin_FindsTheCreatedAuditRow()
    {
        var adminClient = await _factory.CreateAuthenticatedClientAsync(UserRole.Admin);
        var name = $"Audited Org {Guid.NewGuid():N}";
        var createResponse = await adminClient.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest(name, OrganizationType.Customer));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<OrganizationDetailDto>();

        var result = await adminClient.GetFromJsonAsync<PagedResult<AuditLogDto>>(
            $"/api/audit-logs?entityName=Organization&entityId={created!.Id}&action=Created");

        Assert.NotNull(result);
        Assert.Contains(result!.Items, a => a.EntityId == created.Id && a.Action == "Created" && a.EntityName == "Organization");
    }

    /// <summary>AuditLog.Read is Admin-only (see RolePermissions' doc comment) — a Manager token, which already has User.Manage/Project.Manage/Team.Manage, must still be refused here.</summary>
    [Fact]
    public async Task Search_AsAManager_Returns403()
    {
        var managerClient = await _factory.CreateAuthenticatedClientAsync(UserRole.Manager);

        var response = await managerClient.GetAsync("/api/audit-logs");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
