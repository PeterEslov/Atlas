using System.Text.Json;
using Atlas.Application.Common.Interfaces;
using Atlas.Application.Tickets.Events;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;

namespace Atlas.Infrastructure.Messaging;

/// <summary>
/// Publishes <see cref="TicketAssignedEvent"/> to the "atlas-ticket-events"
/// Service Bus topic. The <see cref="ServiceBusSender"/> is created once per
/// call rather than held as a long-lived field — cheap to construct (it's a
/// thin wrapper over the shared, already-connected <see cref="ServiceBusClient"/>
/// registered as a singleton in DependencyInjection.AddMessaging), and this
/// way there's exactly one sender per publish rather than a sender this
/// service has to remember to dispose of itself.
/// </summary>
public sealed class ServiceBusTicketEventPublisher : ITicketEventPublisher
{
    private const string TopicName = "atlas-ticket-events";
    private const string TicketAssignedEventType = "TicketAssigned";

    private readonly ServiceBusClient _client;
    private readonly ILogger<ServiceBusTicketEventPublisher> _logger;

    public ServiceBusTicketEventPublisher(ServiceBusClient client, ILogger<ServiceBusTicketEventPublisher> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task PublishTicketAssignedAsync(TicketAssignedEvent @event, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(@event);
        var message = new ServiceBusMessage(body)
        {
            // Same event, generated once by the caller (TicketService.AssignAsync)
            // rather than regenerated here — so if the SDK's own send retry
            // policy resends this exact message after a transient fault, it
            // resends with the same MessageId both times, and the topic's
            // duplicate-detection window (enabled when the topic itself was
            // created — see docs/AZURE_DEPLOYMENT.md section 9) recognizes the
            // second send as a retry of the same event rather than a second,
            // distinct one.
            MessageId = @event.EventId.ToString(),
            Subject = TicketAssignedEventType,
            ContentType = "application/json",
        };
        message.ApplicationProperties["EventType"] = TicketAssignedEventType;

        // Publishing is deliberately not allowed to fail the HTTP request that
        // triggered it: TicketService.AssignAsync calls this only *after*
        // SaveChangesAsync has already committed the assignment, so the
        // assignment itself is a done deal by the time we get here. A
        // real-time notification that never goes out because Service Bus had
        // a bad moment is a strictly smaller problem than telling the caller
        // their (already-successful) assignment failed. There's no outbox
        // pattern yet to guarantee the event eventually gets published even
        // across a restart between the DB commit and this call — a known,
        // documented gap (see docs/ARCHITECTURE.md), not an oversight.
        try
        {
            await using var sender = _client.CreateSender(TopicName);
            await sender.SendMessageAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Failed to publish TicketAssigned event {EventId} for ticket {TicketId}; the assignment itself already succeeded",
                @event.EventId, @event.TicketId);
        }
    }
}
