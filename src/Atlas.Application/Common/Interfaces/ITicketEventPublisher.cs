using Atlas.Application.Tickets.Events;

namespace Atlas.Application.Common.Interfaces;

/// <summary>
/// Publishes ticket domain events to Service Bus (Del 12). Scoped to
/// <see cref="Events.TicketAssignedEvent"/> specifically rather than a
/// generic <c>PublishAsync&lt;T&gt;(string topic, T message)</c> — the same
/// "narrow, purpose-built interface" choice <see cref="ITicketRepository"/>
/// already documents for itself. A second event type, when one actually
/// shows up, gets its own method here (or its own interface, if the two
/// genuinely have nothing in common) rather than a one-size-fits-all
/// abstraction guessed at before there's a second real use case to shape it
/// against.
/// </summary>
public interface ITicketEventPublisher
{
    Task PublishTicketAssignedAsync(TicketAssignedEvent @event, CancellationToken cancellationToken);
}
