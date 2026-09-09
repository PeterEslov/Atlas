using Atlas.Domain.Common;
using Atlas.Domain.Exceptions;

namespace Atlas.Domain.Entities;

/// <summary>A comment left on a <see cref="Ticket"/> by an agent, manager or the customer.</summary>
public sealed class TicketComment : Entity
{
    public Guid TicketId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string Body { get; private set; } = string.Empty;

    /// <summary>Internal notes are visible to agents/managers only, never to the customer.</summary>
    public bool IsInternal { get; private set; }

    private TicketComment()
    {
        // Required by EF Core for materialization.
    }

    internal static TicketComment Create(Guid ticketId, Guid authorUserId, string body, bool isInternal = false)
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new DomainException("Comment body cannot be empty.");

        if (body.Length > 4000)
            throw new DomainException("Comment body cannot exceed 4000 characters.");

        return new TicketComment
        {
            TicketId = ticketId,
            AuthorUserId = authorUserId,
            Body = body.Trim(),
            IsInternal = isInternal
        };
    }
}
