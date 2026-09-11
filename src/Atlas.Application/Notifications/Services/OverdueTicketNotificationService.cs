using Atlas.Application.Common.Interfaces;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Notifications.Services;

/// <summary>
/// Orchestrates the overdue-ticket check: loads the current set of overdue
/// tickets via the repository, filters out ones already notified about,
/// creates a Notification per remaining ticket via the domain factory, and
/// persists via the unit of work — the same load/mutate-via-domain/save shape
/// every other service in this project follows (see TicketService), just
/// without a single aggregate root at the center of it this time.
/// </summary>
public sealed class OverdueTicketNotificationService : IOverdueTicketNotificationService
{
    private readonly ITicketRepository _ticketRepository;
    private readonly INotificationRepository _notificationRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<OverdueTicketNotificationService> _logger;

    public OverdueTicketNotificationService(
        ITicketRepository ticketRepository,
        INotificationRepository notificationRepository,
        IUnitOfWork unitOfWork,
        ILogger<OverdueTicketNotificationService> logger)
    {
        _ticketRepository = ticketRepository;
        _notificationRepository = notificationRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<int> NotifyOverdueTicketsAsync(CancellationToken cancellationToken)
    {
        var overdueTickets = await _ticketRepository.GetOverdueAsync(cancellationToken);
        if (overdueTickets.Count == 0)
        {
            _logger.LogInformation("Overdue-ticket check: nothing overdue right now");
            return 0;
        }

        // One batch query instead of one "does ticket X already have a
        // notification" query per ticket — see the doc comment on
        // GetNotifiedTicketIdsAsync.
        var ticketIds = overdueTickets.Select(t => t.Id).ToList();
        var alreadyNotified = await _notificationRepository.GetNotifiedTicketIdsAsync(
            NotificationType.TicketOverdue, ticketIds, cancellationToken);

        var created = 0;
        foreach (var ticket in overdueTickets)
        {
            if (alreadyNotified.Contains(ticket.Id))
            {
                // Already notified once for this overdue period — this worker
                // fires a single reminder per ticket, not a fresh one on every
                // tick for as long as it stays overdue. A ticket that gets
                // reopened after being closed, and later goes overdue again,
                // is a genuinely new overdue period: TicketService.ReopenAsync
                // clears ResolvedAtUtc/ClosedAtUtc but a fresh DueAtUtc would
                // need setting for it to become overdue again in the first
                // place, which is a separate, not-yet-built capability — so
                // this simple "ever notified" check is correct for everything
                // the domain can actually do today.
                continue;
            }

            // GetOverdueAsync already filters to AssignedToUserId != null, so
            // there's always a recipient here.
            var message = $"Ärendet \"{ticket.Title}\" är försenat (förfallodatum {ticket.DueAtUtc:yyyy-MM-dd}).";
            var notification = Notification.Create(ticket.AssignedToUserId!.Value, NotificationType.TicketOverdue, message, ticket.Id);
            await _notificationRepository.AddAsync(notification, cancellationToken);
            created++;
        }

        if (created > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Overdue-ticket check: {OverdueCount} ticket(s) overdue, {CreatedCount} new notification(s) created",
            overdueTickets.Count, created);

        return created;
    }
}
