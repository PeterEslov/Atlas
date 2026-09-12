using System.Net;
using System.Net.Http.Json;
using Atlas.Api.IntegrationTests.Infrastructure;
using Atlas.Application.Tickets.Dtos;
using Atlas.Domain.Enums;
using Xunit;

namespace Atlas.Api.IntegrationTests.Tickets;

[Trait("Category", "Integration")]
[Collection(ApiCollection.Name)]
public class TicketsEndpointsTests
{
    private readonly ApiFactory _factory;

    public TicketsEndpointsTests(ApiFactory factory) => _factory = factory;

    private async Task<TicketDetailDto> CreateTicketAsync(HttpClient client, string title)
    {
        var response = await client.PostAsJsonAsync("/api/tickets",
            new CreateTicketRequest(title, "Integration test ticket", _factory.SeededOrganizationId, TicketPriority.Medium, ProjectId: null, DueAtUtc: null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TicketDetailDto>())!;
    }

    [Fact]
    public async Task CreateThenAssignThenChangeStatus_AsAManagerAndAgent_ProducesTheExpectedTicketState()
    {
        var managerClient = await _factory.CreateAuthenticatedClientAsync(UserRole.Manager);
        var agentAuth = await AuthTestHelper.RegisterAsync(_factory.CreateClient(), _factory.SeededOrganizationId, UserRole.Agent);

        var ticket = await CreateTicketAsync(managerClient, $"Printer on fire {Guid.NewGuid():N}");
        Assert.Equal(TicketStatus.New, ticket.Status);

        var assignResponse = await managerClient.PostAsJsonAsync($"/api/tickets/{ticket.Id}/assign", new AssignTicketRequest(agentAuth.UserId));
        Assert.Equal(HttpStatusCode.OK, assignResponse.StatusCode);
        var assigned = await assignResponse.Content.ReadFromJsonAsync<TicketDetailDto>();
        Assert.Equal(agentAuth.UserId, assigned!.AssignedToUserId);

        var statusResponse = await managerClient.PostAsJsonAsync($"/api/tickets/{ticket.Id}/status", new ChangeTicketStatusRequest(TicketStatus.InProgress));
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        Assert.Equal(TicketStatus.InProgress, (await statusResponse.Content.ReadFromJsonAsync<TicketDetailDto>())!.Status);
    }

    /// <summary>
    /// The one thing Atlas.Application.Tests' mocked TicketServiceTests
    /// explicitly could NOT prove (see its AddTagAsync_... doc comment):
    /// that TicketTag.Tag's navigation property actually resolves to the
    /// real tag name once a real EF Core DbContext is tracking both rows.
    /// The mocked unit test could only assert the TagId was right; this one
    /// asserts the human-readable name TicketsController.GetById actually
    /// returns.
    /// </summary>
    [Fact]
    public async Task AddTag_ThenGetById_ReturnsTheResolvedTagName()
    {
        var managerClient = await _factory.CreateAuthenticatedClientAsync(UserRole.Manager);
        var ticket = await CreateTicketAsync(managerClient, $"Needs a billing tag {Guid.NewGuid():N}");
        var tagName = $"billing-{Guid.NewGuid():N}";

        var addResponse = await managerClient.PostAsJsonAsync($"/api/tickets/{ticket.Id}/tags", new AddTicketTagRequest(tagName));
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);

        var fetched = await managerClient.GetFromJsonAsync<TicketDetailDto>($"/api/tickets/{ticket.Id}");
        Assert.Contains(tagName, fetched!.Tags);

        var removeResponse = await managerClient.PostAsJsonAsync($"/api/tickets/{ticket.Id}/tags/remove", new RemoveTicketTagRequest(tagName));
        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);
        Assert.DoesNotContain(tagName, (await removeResponse.Content.ReadFromJsonAsync<TicketDetailDto>())!.Tags);
    }

    [Fact]
    public async Task Assign_AsACustomer_Returns403()
    {
        var managerClient = await _factory.CreateAuthenticatedClientAsync(UserRole.Manager);
        var ticket = await CreateTicketAsync(managerClient, $"Not assignable by a customer {Guid.NewGuid():N}");
        var customerClient = await _factory.CreateAuthenticatedClientAsync(UserRole.Customer);

        var response = await customerClient.PostAsJsonAsync($"/api/tickets/{ticket.Id}/assign", new AssignTicketRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_AsAnAgent_Returns403_ButAsAManager_Returns204()
    {
        var managerClient = await _factory.CreateAuthenticatedClientAsync(UserRole.Manager);
        var ticket = await CreateTicketAsync(managerClient, $"Delete me {Guid.NewGuid():N}");
        var agentClient = await _factory.CreateAuthenticatedClientAsync(UserRole.Agent);

        var forbidden = await agentClient.DeleteAsync($"/api/tickets/{ticket.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var deleted = await managerClient.DeleteAsync($"/api/tickets/{ticket.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var getAfterDelete = await managerClient.GetAsync($"/api/tickets/{ticket.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    /// <summary>
    /// Deliberately asserts ">= 1", not an exact count: GetStats is a Redis
    /// cache-aside read (Del 13, Redis:StatsCacheTtlSeconds = 30 in
    /// appsettings.Testing.json) shared by every test in this class against
    /// the same SeededOrganizationId, so a cache hit here can legitimately
    /// return a snapshot from up to 30 seconds ago that predates this
    /// specific ticket — that staleness is the cache-aside pattern working
    /// as designed (see TicketStatsDto.GeneratedAtUtc's doc comment), not a
    /// bug to assert against. Every ticket created anywhere in this test
    /// class belongs to the same seeded organization, so the count is always
    /// at least 1 by the time this test runs regardless of which snapshot
    /// comes back.
    /// </summary>
    [Fact]
    public async Task GetStats_ForTheCallersOrganization_ReturnsCountsIncludingAtLeastOneTicket()
    {
        var managerClient = await _factory.CreateAuthenticatedClientAsync(UserRole.Manager);
        await CreateTicketAsync(managerClient, $"Counted in stats {Guid.NewGuid():N}");

        var stats = await managerClient.GetFromJsonAsync<TicketStatsDto>("/api/tickets/stats");

        Assert.NotNull(stats);
        Assert.True(stats!.TotalCount >= 1);
    }
}
