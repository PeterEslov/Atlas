using Atlas.Application.Notifications.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atlas.Worker;

/// <summary>
/// Runs for the lifetime of the process, checking on a timer for tickets that
/// have become overdue and turning each into a Notification row — the first
/// thing in the project that actually writes to the Notifications table,
/// which has existed in the schema since Fas 1 but sat unused until now.
///
/// A BackgroundService is registered once and runs once, for the whole life
/// of the process — but everything it calls into (the notification service,
/// the repositories, the DbContext) is scoped, and EF Core's DbContext is
/// explicitly not meant to be shared across concurrent operations or held
/// for a process's entire lifetime. IServiceScopeFactory is the standard
/// bridge: a fresh scope (and therefore a fresh DbContext) is created for
/// every single tick of the timer, used once, and disposed — exactly like a
/// web request gets its own scope in Atlas.Api, just triggered by a timer
/// instead of an HTTP request.
/// </summary>
public sealed class OverdueTicketWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OverdueTicketWorker> _logger;
    private readonly TimeSpan _pollingInterval;

    public OverdueTicketWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<OverdueTicketWorkerOptions> options,
        ILogger<OverdueTicketWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _pollingInterval = TimeSpan.FromMinutes(Math.Max(1, options.Value.PollingIntervalMinutes));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Overdue-ticket worker started, checking every {IntervalMinutes} minute(s)",
            _pollingInterval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var notificationService = scope.ServiceProvider.GetRequiredService<IOverdueTicketNotificationService>();
                await notificationService.NotifyOverdueTicketsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A single failed check (e.g. a transient SQL fault beyond
                // what EnableRetryOnFailure already absorbs) should never
                // crash the whole worker process — log it and try again on
                // the next tick, the same "keep going" philosophy any
                // long-running service needs.
                _logger.LogError(ex, "Overdue-ticket check failed; will retry on the next tick");
            }

            try
            {
                await Task.Delay(_pollingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown (Ctrl+C / host stopping) — not an error.
            }
        }
    }
}
