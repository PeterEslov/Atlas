using Atlas.Application.Common.Interfaces;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="INotificationRepository"/>.</summary>
public sealed class NotificationRepository : INotificationRepository
{
    private readonly AtlasDbContext _dbContext;

    public NotificationRepository(AtlasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HashSet<Guid>> GetNotifiedTicketIdsAsync(NotificationType type, IReadOnlyCollection<Guid> ticketIds, CancellationToken cancellationToken)
    {
        if (ticketIds.Count == 0)
        {
            return [];
        }

        var notified = await _dbContext.Notifications
            .AsNoTracking()
            .Where(n => n.Type == type && n.RelatedTicketId != null && ticketIds.Contains(n.RelatedTicketId.Value))
            .Select(n => n.RelatedTicketId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        return notified.ToHashSet();
    }

    public async Task AddAsync(Notification notification, CancellationToken cancellationToken)
    {
        // Explicit Add(), not implicit graph discovery — same reasoning as
        // TicketRepository.AddAttachmentAsync (Del 10): Notification has a
        // client-generated Guid key, and here there's no already-tracked
        // parent aggregate to discover it through at all (unlike Attachment,
        // which at least hangs off a loaded Ticket), so there is no implicit
        // path that could plausibly infer EntityState.Added on its own.
        await _dbContext.Notifications.AddAsync(notification, cancellationToken);
    }
}
