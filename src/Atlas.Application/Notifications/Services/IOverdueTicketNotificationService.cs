namespace Atlas.Application.Notifications.Services;

public interface IOverdueTicketNotificationService
{
    /// <summary>
    /// Finds every overdue, assigned ticket that doesn't already have a
    /// TicketOverdue notification, creates one for each, and saves. Returns
    /// how many were created — purely so the caller (Atlas.Worker) has
    /// something concrete to log every time it runs.
    /// </summary>
    Task<int> NotifyOverdueTicketsAsync(CancellationToken cancellationToken);
}
