using Atlas.Domain.Common;
using Atlas.Domain.Enums;

namespace Atlas.Domain.Entities;

/// <summary>
/// An in-app/email notification queued for a user. Created by domain/application
/// logic when something notification-worthy happens (see Del 9's overdue-ticket
/// worker and Del 10's Service Bus events); delivery itself is an infrastructure
/// concern handled elsewhere.
/// </summary>
public sealed class Notification : Entity
{
    public Guid RecipientUserId { get; private set; }
    public NotificationType Type { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public Guid? RelatedTicketId { get; private set; }
    public bool IsRead { get; private set; }
    public DateTime? ReadAtUtc { get; private set; }

    private Notification()
    {
        // Required by EF Core for materialization.
    }

    public static Notification Create(Guid recipientUserId, NotificationType type, string message, Guid? relatedTicketId = null) =>
        new()
        {
            RecipientUserId = recipientUserId,
            Type = type,
            Message = message,
            RelatedTicketId = relatedTicketId
        };

    public void MarkAsRead()
    {
        if (IsRead) return;
        IsRead = true;
        ReadAtUtc = DateTime.UtcNow;
    }
}
