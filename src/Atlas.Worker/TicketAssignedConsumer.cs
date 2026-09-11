using System.Text.Json;
using Atlas.Application.Common.Interfaces;
using Atlas.Application.Tickets.Events;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atlas.Worker;

/// <summary>
/// Listens on the "atlas-ticket-events" topic's "atlas-notifications"
/// subscription (Del 12) and turns each <see cref="TicketAssignedEvent"/> it
/// receives into a Notification row — the real-time counterpart to
/// OverdueTicketWorker's polling loop above: that one wakes up on a timer and
/// asks "what's changed since last time?", this one is pushed a message the
/// moment TicketService.AssignAsync publishes it.
///
/// Same IServiceScopeFactory pattern as OverdueTicketWorker for the same
/// reason (a fresh scope, and therefore a fresh DbContext, per message rather
/// than one held for the life of the process) — except here a "tick" is one
/// received message instead of one timer interval.
///
/// Known, documented gap (see TicketAssignedEvent's doc comment): Service Bus
/// is at-least-once delivery, not exactly-once. If this process crashes after
/// SaveChangesAsync commits the Notification row but before CompleteMessageAsync
/// acknowledges the message, Service Bus redelivers it and a second, duplicate
/// Notification is created — unlike OverdueTicketNotificationService, which
/// guards against duplicates by querying GetNotifiedTicketIdsAsync before
/// creating one. Closing that gap here would mean the same kind of check
/// (e.g. keyed on the event's EventId, which isn't stored anywhere on
/// Notification today) — a real follow-up, not implemented in Del 12.
/// </summary>
public sealed class TicketAssignedConsumer : BackgroundService
{
    private const string TopicName = "atlas-ticket-events";
    private const string SubscriptionName = "atlas-notifications";

    private readonly ServiceBusClient _client;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TicketAssignedConsumer> _logger;
    private ServiceBusProcessor? _processor;

    public TicketAssignedConsumer(ServiceBusClient client, IServiceScopeFactory scopeFactory, ILogger<TicketAssignedConsumer> logger)
    {
        _client = client;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // AutoCompleteMessages: false — completion is explicit, at the end of
        // ProcessMessageAsync below, only once the Notification row has
        // actually been saved. Auto-completing on successful handler return
        // would be the same thing here (the handler doesn't throw past its
        // own try/catch), but being explicit makes the "only complete after
        // the write succeeds" ordering obvious from reading the processor
        // options rather than relying on the handler's control flow alone.
        _processor = _client.CreateProcessor(TopicName, SubscriptionName, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
        });

        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);

        _logger.LogInformation(
            "TicketAssigned consumer started, listening on {Topic}/{Subscription}",
            TopicName, SubscriptionName);

        // StartProcessingAsync above returns immediately — the processor
        // pumps messages on its own background threads from here on. This
        // just has to keep ExecuteAsync (and therefore the whole
        // BackgroundService) alive until the host asks it to stop; the
        // actual stopping happens in StopAsync below, not by this delay
        // completing on its own.
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown (Ctrl+C / host stopping) — not an error.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        try
        {
            var @event = args.Message.Body.ToObjectFromJson<TicketAssignedEvent>();
            if (@event is null)
            {
                // Malformed body — no amount of retrying will make it
                // deserialize, so dead-lettering (rather than abandoning,
                // which would just get it redelivered forever) takes it out
                // of the active queue for someone to look at later.
                _logger.LogWarning(
                    "Received a TicketAssigned message ({MessageId}) that deserialized to null; dead-lettering",
                    args.Message.MessageId);
                await args.DeadLetterMessageAsync(args.Message, cancellationToken: args.CancellationToken);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var notificationRepository = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var message = $"Du har tilldelats ärendet \"{@event.TicketTitle}\".";
            var notification = Notification.Create(@event.AssignedToUserId, NotificationType.TicketAssigned, message, @event.TicketId);
            await notificationRepository.AddAsync(notification, args.CancellationToken);
            await unitOfWork.SaveChangesAsync(args.CancellationToken);

            await args.CompleteMessageAsync(args.Message, args.CancellationToken);

            _logger.LogInformation(
                "TicketAssigned event {EventId} for ticket {TicketId} turned into a notification for user {AssignedToUserId}",
                @event.EventId, @event.TicketId, @event.AssignedToUserId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Left uncompleted (abandoned rather than completed or
            // dead-lettered) — Service Bus redelivers it, which is the right
            // outcome for a transient failure (e.g. a momentary SQL fault).
            // A message stuck failing forever eventually exhausts its max
            // delivery count and lands in the subscription's own dead-letter
            // queue automatically, so this doesn't need to track retry
            // counts itself.
            _logger.LogError(ex, "Failed to process TicketAssigned message {MessageId}; abandoning for redelivery", args.Message.MessageId);
            await args.AbandonMessageAsync(args.Message, cancellationToken: CancellationToken.None);
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Service Bus processor error (source: {ErrorSource})", args.ErrorSource);
        return Task.CompletedTask;
    }
}
