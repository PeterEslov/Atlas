using Atlas.Domain.Common;
using Atlas.Domain.Enums;
using Atlas.Domain.Exceptions;

namespace Atlas.Domain.Entities;

/// <summary>
/// Aggregate root for the ticketing bounded context. Comments, history entries and
/// tag assignments are all modified exclusively through methods on this class, so
/// that every state change is validated and captured in <see cref="History"/>.
/// </summary>
public sealed class Ticket : Entity
{
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public TicketStatus Status { get; private set; }
    public TicketPriority Priority { get; private set; }

    /// <summary>The customer (or internal) organization this ticket was raised for.</summary>
    public Guid OrganizationId { get; private set; }
    public Guid? ProjectId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid? AssignedToUserId { get; private set; }

    public DateTime? DueAtUtc { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }

    // Navigation properties. Populated by EF Core only when explicitly requested
    // via .Include(...) — null otherwise. They exist purely to make read queries
    // convenient; every state change still goes exclusively through this class's
    // own methods, never through these references.
    public Organization? Organization { get; private set; }
    public Project? Project { get; private set; }
    public User? CreatedByUser { get; private set; }
    public User? AssignedToUser { get; private set; }

    private readonly List<TicketComment> _comments = [];
    public IReadOnlyCollection<TicketComment> Comments => _comments.AsReadOnly();

    private readonly List<TicketHistory> _history = [];
    public IReadOnlyCollection<TicketHistory> History => _history.AsReadOnly();

    private readonly List<TicketTag> _tags = [];
    public IReadOnlyCollection<TicketTag> Tags => _tags.AsReadOnly();

    private readonly List<Attachment> _attachments = [];
    public IReadOnlyCollection<Attachment> Attachments => _attachments.AsReadOnly();

    /// <summary>True when the ticket has an open due date in the past and is not yet resolved/closed/cancelled.</summary>
    public bool IsOverdue =>
        DueAtUtc.HasValue
        && DateTime.UtcNow > DueAtUtc.Value
        && Status is not (TicketStatus.Resolved or TicketStatus.Closed or TicketStatus.Cancelled);

    private Ticket()
    {
        // Required by EF Core for materialization.
    }

    public static Ticket Create(
        string title,
        string description,
        Guid organizationId,
        Guid createdByUserId,
        TicketPriority priority = TicketPriority.Medium,
        Guid? projectId = null,
        DateTime? dueAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("Ticket title cannot be empty.");

        if (title.Length > 200)
            throw new DomainException("Ticket title cannot exceed 200 characters.");

        var ticket = new Ticket
        {
            Title = title.Trim(),
            Description = description?.Trim() ?? string.Empty,
            Status = TicketStatus.New,
            Priority = priority,
            OrganizationId = organizationId,
            CreatedByUserId = createdByUserId,
            ProjectId = projectId,
            DueAtUtc = dueAtUtc
        };

        ticket._history.Add(TicketHistory.Record(ticket.Id, createdByUserId, nameof(Status), null, TicketStatus.New.ToString()));
        return ticket;
    }

    public void AssignTo(Guid userId, Guid actorUserId)
    {
        EnsureNotClosed();

        if (AssignedToUserId == userId) return;

        var old = AssignedToUserId;
        AssignedToUserId = userId;
        if (Status == TicketStatus.New) Status = TicketStatus.Open;
        MarkModified();

        _history.Add(TicketHistory.Record(Id, actorUserId, nameof(AssignedToUserId), old?.ToString(), userId.ToString()));
    }

    public void ChangeStatus(TicketStatus newStatus, Guid actorUserId)
    {
        EnsureNotClosed();

        if (newStatus == Status) return;

        var old = Status;
        Status = newStatus;
        ResolvedAtUtc = newStatus == TicketStatus.Resolved ? DateTime.UtcNow : ResolvedAtUtc;
        ClosedAtUtc = newStatus is TicketStatus.Closed or TicketStatus.Cancelled ? DateTime.UtcNow : ClosedAtUtc;
        MarkModified();

        _history.Add(TicketHistory.Record(Id, actorUserId, nameof(Status), old.ToString(), newStatus.ToString()));
    }

    public void ChangePriority(TicketPriority newPriority, Guid actorUserId, string? escalationReason = null)
    {
        EnsureNotClosed();

        if (newPriority == TicketPriority.Critical && string.IsNullOrWhiteSpace(escalationReason))
            throw new DomainException("A reason is required when escalating a ticket to Critical priority.");

        if (newPriority == Priority) return;

        var old = Priority;
        Priority = newPriority;
        MarkModified();

        var note = newPriority == TicketPriority.Critical ? $"{old} -> {newPriority} ({escalationReason})" : newPriority.ToString();
        _history.Add(TicketHistory.Record(Id, actorUserId, nameof(Priority), old.ToString(), note));
    }

    public void Reopen(Guid actorUserId)
    {
        if (Status is not (TicketStatus.Closed or TicketStatus.Cancelled))
            throw new DomainException("Only a closed or cancelled ticket can be reopened.");

        var old = Status;
        Status = TicketStatus.Open;
        ResolvedAtUtc = null;
        ClosedAtUtc = null;
        MarkModified();

        _history.Add(TicketHistory.Record(Id, actorUserId, nameof(Status), old.ToString(), TicketStatus.Open.ToString()));
    }

    public TicketComment AddComment(Guid authorUserId, string body, bool isInternal = false)
    {
        var comment = TicketComment.Create(Id, authorUserId, body, isInternal);
        _comments.Add(comment);
        MarkModified();
        return comment;
    }

    public void AddTag(Guid tagId)
    {
        if (_tags.Any(t => t.TagId == tagId)) return;
        _tags.Add(TicketTag.Create(Id, tagId));
        MarkModified();
    }

    public void RemoveTag(Guid tagId)
    {
        var existing = _tags.FirstOrDefault(t => t.TagId == tagId);
        if (existing is null) return;
        _tags.Remove(existing);
        MarkModified();
    }

    public Attachment AddAttachment(string fileName, string contentType, long sizeInBytes, string blobName, Guid uploadedByUserId)
    {
        var attachment = Attachment.Create(Id, fileName, contentType, sizeInBytes, blobName, uploadedByUserId);
        _attachments.Add(attachment);
        MarkModified();
        return attachment;
    }

    private void EnsureNotClosed()
    {
        if (Status is TicketStatus.Closed or TicketStatus.Cancelled)
            throw new DomainException($"Ticket {Id} is {Status} and must be reopened before it can be modified further.");
    }
}
