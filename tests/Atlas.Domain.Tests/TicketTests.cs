using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Atlas.Domain.Exceptions;
using Xunit;

namespace Atlas.Domain.Tests;

public class TicketTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid CreatedByUserId = Guid.NewGuid();
    private static readonly Guid AgentUserId = Guid.NewGuid();

    private static Ticket CreateTicket(TicketPriority priority = TicketPriority.Medium) =>
        Ticket.Create("Customer cannot login", "ACME reports a 500 on the login page.", OrganizationId, CreatedByUserId, priority);

    [Fact]
    public void Create_SetsStatusToNew_AndRecordsInitialHistoryEntry()
    {
        var ticket = CreateTicket();

        Assert.Equal(TicketStatus.New, ticket.Status);
        Assert.Single(ticket.History);
        Assert.Equal(nameof(Ticket.Status), ticket.History.Single().FieldName);
    }

    [Fact]
    public void Create_WithEmptyTitle_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() =>
            Ticket.Create(" ", "description", OrganizationId, CreatedByUserId));
    }

    [Fact]
    public void CanAssignTicket()
    {
        var ticket = CreateTicket();

        ticket.AssignTo(AgentUserId, CreatedByUserId);

        Assert.Equal(AgentUserId, ticket.AssignedToUserId);
        Assert.Equal(TicketStatus.Open, ticket.Status); // New -> Open on first assignment
        Assert.Contains(ticket.History, h => h.FieldName == nameof(Ticket.AssignedToUserId));
    }

    [Fact]
    public void CannotAssignClosedTicket()
    {
        var ticket = CreateTicket();
        ticket.AssignTo(AgentUserId, CreatedByUserId);
        ticket.ChangeStatus(TicketStatus.Resolved, AgentUserId);
        ticket.ChangeStatus(TicketStatus.Closed, AgentUserId);

        Assert.Throws<DomainException>(() => ticket.AssignTo(Guid.NewGuid(), AgentUserId));
    }

    [Fact]
    public void CriticalTicketRequiresReason()
    {
        var ticket = CreateTicket();

        Assert.Throws<DomainException>(() => ticket.ChangePriority(TicketPriority.Critical, AgentUserId));

        ticket.ChangePriority(TicketPriority.Critical, AgentUserId, "Production login outage affecting all ACME users.");
        Assert.Equal(TicketPriority.Critical, ticket.Priority);
    }

    [Fact]
    public void ChangeStatus_ToResolved_SetsResolvedAtUtc()
    {
        var ticket = CreateTicket();

        ticket.ChangeStatus(TicketStatus.Resolved, AgentUserId);

        Assert.NotNull(ticket.ResolvedAtUtc);
    }

    [Fact]
    public void Reopen_OnClosedTicket_ResetsStatusAndClearsTimestamps()
    {
        var ticket = CreateTicket();
        ticket.ChangeStatus(TicketStatus.Resolved, AgentUserId);
        ticket.ChangeStatus(TicketStatus.Closed, AgentUserId);

        ticket.Reopen(AgentUserId);

        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Null(ticket.ResolvedAtUtc);
        Assert.Null(ticket.ClosedAtUtc);
    }

    [Fact]
    public void Reopen_OnNonClosedTicket_ThrowsDomainException()
    {
        var ticket = CreateTicket();

        Assert.Throws<DomainException>(() => ticket.Reopen(AgentUserId));
    }

    [Fact]
    public void AddComment_WithEmptyBody_ThrowsDomainException()
    {
        var ticket = CreateTicket();

        Assert.Throws<DomainException>(() => ticket.AddComment(AgentUserId, "   "));
    }

    [Fact]
    public void AddTag_WithNewTagId_AddsIt()
    {
        var ticket = CreateTicket();
        var tagId = Guid.NewGuid();

        ticket.AddTag(tagId);

        Assert.Single(ticket.Tags);
        Assert.Equal(tagId, ticket.Tags.Single().TagId);
    }

    [Fact]
    public void AddTag_SameTagTwice_IsANoOp()
    {
        // Unlike Project.AddMember/Team.AddMember, a duplicate tag doesn't throw
        // — tagging something that's already tagged isn't a caller mistake worth
        // surfacing, it's just idempotent.
        var ticket = CreateTicket();
        var tagId = Guid.NewGuid();
        ticket.AddTag(tagId);

        ticket.AddTag(tagId);

        Assert.Single(ticket.Tags);
    }

    [Fact]
    public void RemoveTag_ExistingTag_RemovesIt()
    {
        var ticket = CreateTicket();
        var tagId = Guid.NewGuid();
        ticket.AddTag(tagId);

        ticket.RemoveTag(tagId);

        Assert.Empty(ticket.Tags);
    }

    [Fact]
    public void RemoveTag_UnknownTag_IsANoOp()
    {
        var ticket = CreateTicket();

        var exception = Record.Exception(() => ticket.RemoveTag(Guid.NewGuid()));

        Assert.Null(exception);
    }

    [Fact]
    public void IsOverdue_WhenDueDateInPastAndNotResolved_ReturnsTrue()
    {
        var ticket = Ticket.Create(
            "Overdue example",
            "description",
            OrganizationId,
            CreatedByUserId,
            TicketPriority.Medium,
            dueAtUtc: DateTime.UtcNow.AddDays(-1));

        Assert.True(ticket.IsOverdue);
    }

    [Fact]
    public void IsOverdue_WhenResolved_ReturnsFalseEvenIfPastDueDate()
    {
        var ticket = Ticket.Create(
            "Overdue but resolved",
            "description",
            OrganizationId,
            CreatedByUserId,
            TicketPriority.Medium,
            dueAtUtc: DateTime.UtcNow.AddDays(-1));

        ticket.ChangeStatus(TicketStatus.Resolved, CreatedByUserId);

        Assert.False(ticket.IsOverdue);
    }
}
