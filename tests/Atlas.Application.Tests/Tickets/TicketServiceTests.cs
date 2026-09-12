using Atlas.Application.Common.Interfaces;
using Atlas.Application.Tickets.Dtos;
using Atlas.Application.Tickets.Events;
using Atlas.Application.Tickets.Services;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Atlas.Application.Tests.Tickets;

/// <summary>
/// TicketService is where the most interesting, previously-only-documented
/// decisions live (cache invalidation is method-by-method, not "invalidate on
/// every write"; a Service Bus publish must never fail an already-successful
/// assignment; an orphaned blob is the acceptable failure direction, not an
/// orphaned database row) — these tests exist specifically to make each of
/// those decisions a regression test, not just exercise the happy path.
///
/// What's deliberately NOT here: ServiceBusTicketEventPublisher's own
/// try/catch-and-log-a-warning behavior (see its doc comment) lives in
/// Atlas.Infrastructure, not in TicketService — from TicketService's own
/// point of view, ITicketEventPublisher.PublishTicketAssignedAsync is a Task
/// that either completes or throws like any other awaited call, with no
/// special handling. Testing the real Service Bus client's failure handling
/// would mean either faking Azure.Messaging.ServiceBus.ServiceBusClient
/// (sealed, not designed to be mocked) or running against a real namespace —
/// exactly why Atlas.Api.IntegrationTests, not a mocked unit test, is where
/// that lives instead.
/// </summary>
public class TicketServiceTests
{
    private readonly Mock<ITicketRepository> _ticketRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IBlobStorageService> _blobStorageService = new();
    private readonly Mock<ITicketEventPublisher> _ticketEventPublisher = new();
    private readonly Mock<ITicketStatsCache> _ticketStatsCache = new();
    private readonly Mock<IAuditLogRepository> _auditLogRepository = new();

    private readonly Guid _organizationId = Guid.NewGuid();
    private readonly Guid _createdByUserId = Guid.NewGuid();
    private readonly Guid _actorUserId = Guid.NewGuid();

    private TicketService CreateSut() => new(
        _ticketRepository.Object,
        _unitOfWork.Object,
        _blobStorageService.Object,
        _ticketEventPublisher.Object,
        _ticketStatsCache.Object,
        _auditLogRepository.Object,
        NullLogger<TicketService>.Instance);

    private Ticket CreateTicket(TicketPriority priority = TicketPriority.Medium)
    {
        var ticket = Ticket.Create("Customer cannot login", "500 on the login page", _organizationId, _createdByUserId, priority);
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, true, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        return ticket;
    }

    [Fact]
    public async Task CreateAsync_InvalidatesTheStatsCacheForTheTicketsOrganization()
    {
        var sut = CreateSut();
        var request = new CreateTicketRequest("Customer cannot login", "500 on the login page", _organizationId, TicketPriority.Medium, null, null);

        await sut.CreateAsync(request, _createdByUserId, CancellationToken.None);

        _ticketStatsCache.Verify(c => c.InvalidateAsync(_organizationId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AssignAsync_PublishesATicketAssignedEvent_ButDeliberatelyDoesNotInvalidateTheStatsCache()
    {
        var ticket = CreateTicket();
        var assignedToUserId = Guid.NewGuid();
        var sut = CreateSut();

        var result = await sut.AssignAsync(ticket.Id, new AssignTicketRequest(assignedToUserId), _actorUserId, CancellationToken.None);

        Assert.Equal(assignedToUserId, result.AssignedToUserId);
        _ticketEventPublisher.Verify(p => p.PublishTicketAssignedAsync(
            It.Is<TicketAssignedEvent>(e => e.TicketId == ticket.Id && e.AssignedToUserId == assignedToUserId && e.ActorUserId == _actorUserId),
            It.IsAny<CancellationToken>()), Times.Once);

        // Who a ticket is assigned to isn't one of TicketStatsDto's counted
        // fields — see the doc comment on TicketService.AssignAsync. Invalidating
        // here would just be an extra Redis round-trip for a write the cached
        // snapshot was never wrong about.
        _ticketStatsCache.Verify(c => c.InvalidateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(TicketStatus.InProgress)]
    [InlineData(TicketStatus.Resolved)]
    public async Task ChangeStatusAsync_InvalidatesTheStatsCache(TicketStatus newStatus)
    {
        var ticket = CreateTicket();
        var sut = CreateSut();

        await sut.ChangeStatusAsync(ticket.Id, new ChangeTicketStatusRequest(newStatus), _actorUserId, CancellationToken.None);

        _ticketStatsCache.Verify(c => c.InvalidateAsync(_organizationId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangePriorityAsync_InvalidatesTheStatsCache()
    {
        var ticket = CreateTicket(TicketPriority.Medium);
        var sut = CreateSut();

        await sut.ChangePriorityAsync(ticket.Id, new ChangeTicketPriorityRequest(TicketPriority.High, null), _actorUserId, CancellationToken.None);

        _ticketStatsCache.Verify(c => c.InvalidateAsync(_organizationId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReopenAsync_OnAClosedTicket_InvalidatesTheStatsCache()
    {
        // ChangeStatus is called directly on the domain object here, not
        // through TicketService.ChangeStatusAsync — so getting the ticket
        // into a Closed state first doesn't itself touch the mocked cache.
        var ticket = CreateTicket();
        ticket.ChangeStatus(TicketStatus.Resolved, _actorUserId);
        ticket.ChangeStatus(TicketStatus.Closed, _actorUserId);
        var sut = CreateSut();

        await sut.ReopenAsync(ticket.Id, _actorUserId, CancellationToken.None);

        _ticketStatsCache.Verify(c => c.InvalidateAsync(_organizationId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WritesADeletedAuditSnapshot_RemovesTheTicket_AndInvalidatesTheStatsCache()
    {
        var ticket = CreateTicket();
        var sut = CreateSut();

        await sut.DeleteAsync(ticket.Id, _actorUserId, CancellationToken.None);

        _auditLogRepository.Verify(r => r.AddAsync(
            It.Is<AuditLog>(a => a.Action == "Deleted"
                && a.EntityName == nameof(Ticket)
                && a.EntityId == ticket.Id
                && a.NewValuesJson == null
                && (a.OldValuesJson ?? string.Empty).Contains("Customer cannot login")),
            It.IsAny<CancellationToken>()), Times.Once);
        _ticketRepository.Verify(r => r.Remove(ticket), Times.Once);
        _ticketStatsCache.Verify(c => c.InvalidateAsync(_organizationId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetStatsAsync_OnACacheHit_ReturnsTheCachedValueWithoutQueryingTheRepository()
    {
        var cached = new TicketStatsDto(_organizationId, 10, 1, 2, 3, 0, 2, 1, 1, 4, 3, 2, 1, 0, DateTime.UtcNow);
        _ticketStatsCache.Setup(c => c.GetAsync(_organizationId, It.IsAny<CancellationToken>())).ReturnsAsync(cached);
        var sut = CreateSut();

        var result = await sut.GetStatsAsync(_organizationId, CancellationToken.None);

        Assert.Same(cached, result);
        _ticketRepository.Verify(r => r.GetStatsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _ticketStatsCache.Verify(c => c.SetAsync(It.IsAny<Guid>(), It.IsAny<TicketStatsDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetStatsAsync_OnACacheMiss_QueriesTheRepositoryAndPopulatesTheCache()
    {
        _ticketStatsCache.Setup(c => c.GetAsync(_organizationId, It.IsAny<CancellationToken>())).ReturnsAsync((TicketStatsDto?)null);
        var fresh = new TicketStatsDto(_organizationId, 5, 1, 1, 1, 0, 1, 1, 0, 2, 2, 1, 0, 0, DateTime.UtcNow);
        _ticketRepository.Setup(r => r.GetStatsAsync(_organizationId, It.IsAny<CancellationToken>())).ReturnsAsync(fresh);
        var sut = CreateSut();

        var result = await sut.GetStatsAsync(_organizationId, CancellationToken.None);

        Assert.Same(fresh, result);
        _ticketStatsCache.Verify(c => c.SetAsync(_organizationId, fresh, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddAttachmentAsync_UploadsTheBlob_ThenRecordsTheAttachment()
    {
        var ticket = CreateTicket();
        var sut = CreateSut();
        using var content = new MemoryStream([1, 2, 3, 4]);

        var result = await sut.AddAttachmentAsync(ticket.Id, "screenshot.png", "image/png", content, 4, _actorUserId, CancellationToken.None);

        Assert.Equal("screenshot.png", result.FileName);
        _blobStorageService.Verify(b => b.UploadAsync(It.IsAny<string>(), content, "image/png", It.IsAny<CancellationToken>()), Times.Once);
        _ticketRepository.Verify(r => r.AddAttachmentAsync(It.IsAny<Attachment>(), It.IsAny<CancellationToken>()), Times.Once);
        // Never a compensating delete on the happy path.
        _blobStorageService.Verify(b => b.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// The exact scenario docs/ARCHITECTURE.md's Del 10 section documents: the
    /// blob upload succeeds, but the database write that should follow it
    /// fails — here, simulated by SaveChangesAsync itself throwing. The
    /// documented tradeoff is that this should leave, at worst, an orphaned
    /// blob (a few cents of storage), never an Attachment row pointing at a
    /// blob that was never actually written — so the compensating delete
    /// below must fire, using the SAME blob name that was just uploaded.
    /// </summary>
    [Fact]
    public async Task AddAttachmentAsync_WhenTheDatabaseWriteFailsAfterTheBlobUpload_DeletesTheOrphanedBlobAndRethrows()
    {
        var ticket = CreateTicket();
        string? uploadedBlobName = null;
        _blobStorageService
            .Setup(b => b.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, Stream, string, CancellationToken>((blobName, _, _, _) => uploadedBlobName = blobName)
            .Returns(Task.CompletedTask);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("simulated database failure"));
        var sut = CreateSut();
        using var content = new MemoryStream([1, 2, 3, 4]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.AddAttachmentAsync(ticket.Id, "screenshot.png", "image/png", content, 4, _actorUserId, CancellationToken.None));

        Assert.NotNull(uploadedBlobName);
        _blobStorageService.Verify(b => b.DeleteAsync(uploadedBlobName!, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Asserts on TicketTag.TagId, not the resolved tag *name*, and that's
    /// deliberate, not an oversight: ToDetailDto reads
    /// <c>t.Tag?.Name ?? t.TagId.ToString()</c>, and <c>TicketTag.Tag</c> only
    /// gets populated by EF Core's relationship-fixup (matching a newly
    /// tracked TicketTag's TagId against an already-tracked Tag instance in
    /// the same DbContext's change tracker) — a real mechanism, but one that
    /// only exists with a real DbContext. With a mocked ITicketRepository and
    /// no DbContext anywhere in this test, that fixup never happens, so
    /// asserting the resolved name here would either fail or, worse, pass for
    /// the wrong reason. See Atlas.Api.IntegrationTests for a test that
    /// exercises the real name-resolution path end-to-end.
    /// </summary>
    [Fact]
    public async Task AddTagAsync_GetOrCreatesTheTagByName_AndAddsItToTheTicket()
    {
        var ticket = CreateTicket();
        var tag = Tag.Create(_organizationId, "billing");
        _ticketRepository.Setup(r => r.GetOrCreateTagAsync(_organizationId, "billing", It.IsAny<CancellationToken>())).ReturnsAsync(tag);
        var sut = CreateSut();

        var result = await sut.AddTagAsync(ticket.Id, new AddTicketTagRequest("billing"), _actorUserId, CancellationToken.None);

        Assert.Single(result.Tags);
        Assert.Contains(ticket.Tags, t => t.TagId == tag.Id);
    }

    [Fact]
    public async Task RemoveTagAsync_ATagNameTheTicketNeverHad_IsANoOp_DoesNotSaveChanges()
    {
        var ticket = CreateTicket();
        _ticketRepository.Setup(r => r.FindTagByNameAsync(_organizationId, "billing", It.IsAny<CancellationToken>())).ReturnsAsync((Tag?)null);
        var sut = CreateSut();

        await sut.RemoveTagAsync(ticket.Id, new RemoveTicketTagRequest("billing"), _actorUserId, CancellationToken.None);

        // FindTagByNameAsync returning null (the tag was never applied, or
        // already removed) must never trigger a get-or-create, and must never
        // save changes for a mutation that never actually happened — see the
        // doc comment on TicketService.RemoveTagAsync.
        _ticketRepository.Verify(r => r.GetOrCreateTagAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
