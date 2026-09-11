using Atlas.Domain.Entities;
using Atlas.Domain.Enums;

namespace Atlas.Application.Common.Interfaces;

/// <summary>
/// Persistence contract for <see cref="Notification"/>. Deliberately narrow —
/// the only two things anything currently needs are "which of these tickets
/// already has a notification of this type" (so Del 11's overdue-ticket
/// worker never sends the same notification twice for the same ticket) and
/// "add a new one". Reading/marking-as-read notifications back for a user is
/// a natural follow-up once something actually surfaces them (an API
/// endpoint, Del 12's Service Bus consumers, ...) — not needed yet.
/// </summary>
public interface INotificationRepository
{
    /// <summary>
    /// Of the given ticket ids, which already have a <paramref name="type"/>
    /// notification recorded against them. A batch check (one query) rather
    /// than one query per ticket, for the same N+1 reason Del 7's
    /// GetMemberCountsAsync exists.
    /// </summary>
    Task<HashSet<Guid>> GetNotifiedTicketIdsAsync(NotificationType type, IReadOnlyCollection<Guid> ticketIds, CancellationToken cancellationToken);

    Task AddAsync(Notification notification, CancellationToken cancellationToken);
}
