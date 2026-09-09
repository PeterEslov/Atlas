using Atlas.Domain.Common;
using Atlas.Domain.Exceptions;

namespace Atlas.Domain.Entities;

/// <summary>A reusable label (e.g. "login", "billing", "outage") that tickets can be tagged with.</summary>
public sealed class Tag : Entity
{
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = string.Empty;

    private readonly List<TicketTag> _ticketTags = [];
    public IReadOnlyCollection<TicketTag> TicketTags => _ticketTags.AsReadOnly();

    private Tag()
    {
        // Required by EF Core for materialization.
    }

    public static Tag Create(Guid organizationId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Tag name cannot be empty.");

        return new Tag
        {
            OrganizationId = organizationId,
            Name = name.Trim().ToLowerInvariant()
        };
    }
}

/// <summary>Join entity linking a <see cref="Ticket"/> to a <see cref="Tag"/> (many-to-many).</summary>
public sealed class TicketTag : Entity
{
    public Guid TicketId { get; private set; }
    public Guid TagId { get; private set; }

    public Ticket? Ticket { get; private set; }
    public Tag? Tag { get; private set; }

    private TicketTag()
    {
        // Required by EF Core for materialization.
    }

    internal static TicketTag Create(Guid ticketId, Guid tagId) =>
        new() { TicketId = ticketId, TagId = tagId };
}
