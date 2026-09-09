using Atlas.Domain.Common;

namespace Atlas.Domain.Entities;

/// <summary>
/// An immutable audit record of a single field change on a <see cref="Ticket"/>.
/// Created only through <see cref="Record"/>, appended by Ticket's own domain
/// methods — never constructed or mutated directly by callers outside the aggregate.
/// </summary>
public sealed class TicketHistory : Entity
{
    public Guid TicketId { get; private set; }
    public Guid ChangedByUserId { get; private set; }
    public string FieldName { get; private set; } = string.Empty;
    public string? OldValue { get; private set; }
    public string? NewValue { get; private set; }
    public DateTime ChangedAtUtc { get; private set; }

    private TicketHistory()
    {
        // Required by EF Core for materialization.
    }

    internal static TicketHistory Record(Guid ticketId, Guid changedByUserId, string fieldName, string? oldValue, string? newValue) =>
        new()
        {
            TicketId = ticketId,
            ChangedByUserId = changedByUserId,
            FieldName = fieldName,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedAtUtc = DateTime.UtcNow
        };
}
