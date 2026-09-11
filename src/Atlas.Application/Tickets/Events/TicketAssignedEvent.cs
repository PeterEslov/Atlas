namespace Atlas.Application.Tickets.Events;

/// <summary>
/// Published to Service Bus (Del 12) when <c>TicketService.AssignAsync</c>
/// successfully assigns a ticket to a user. The wire contract between
/// Atlas.Api (publisher) and Atlas.Worker's consumer (Del 12) — deliberately
/// its own small type, not the same <c>TicketDetailDto</c> the API returns to
/// HTTP callers, so the two can evolve independently: a field the UI needs
/// tomorrow doesn't have to ride along on every message on the wire, and vice
/// versa.
///
/// <paramref name="EventId"/> is generated once per publish attempt (not
/// once per retry) and doubles as the Service Bus message's MessageId, so
/// the topic's duplicate-detection window (enabled when the topic is
/// created — see docs/AZURE_DEPLOYMENT.md section 9) can recognize a
/// sender-side retry of the *same* publish attempt as a duplicate rather
/// than a second, distinct event. It does not protect against the consumer
/// receiving and processing the same message twice (Service Bus is
/// at-least-once delivery, not exactly-once) — see the doc comment on
/// TicketAssignedConsumer for why that's a documented, not-yet-closed gap
/// rather than an oversight.
/// </summary>
public sealed record TicketAssignedEvent(
    Guid EventId,
    Guid TicketId,
    string TicketTitle,
    Guid AssignedToUserId,
    Guid ActorUserId,
    DateTime AssignedAtUtc);
