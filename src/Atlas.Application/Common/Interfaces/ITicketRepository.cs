using Atlas.Application.Tickets.Dtos;
using Atlas.Domain.Entities;

namespace Atlas.Application.Common.Interfaces;

/// <summary>
/// Persistence contract for <see cref="Ticket"/>. Implemented by Atlas.Infrastructure
/// against EF Core / Azure SQL. Kept intentionally narrow (no generic IRepository&lt;T&gt;)
/// because tickets have query needs (filtering, paging, eager-loading comments/history)
/// that a generic abstraction would only get in the way of.
/// </summary>
public interface ITicketRepository
{
    /// <param name="includeDetails">When true, eagerly loads Comments, History, Tags and Attachments.</param>
    Task<Ticket?> GetByIdAsync(Guid id, bool includeDetails, CancellationToken cancellationToken);

    Task<(IReadOnlyList<Ticket> Items, int TotalCount)> SearchAsync(TicketListQuery query, CancellationToken cancellationToken);

    Task AddAsync(Ticket ticket, CancellationToken cancellationToken);

    /// <summary>
    /// No-op for a change-tracked, attached entity — present so the intent is explicit
    /// at call sites and so an alternate implementation (e.g. a unit-test in-memory
    /// repository) has something to override.
    /// </summary>
    void Update(Ticket ticket);

    /// <summary>
    /// Looks up an existing tag by its normalized name within an organization,
    /// without creating anything. Used by RemoveTagAsync — removing "billing"
    /// should never have the side effect of creating a "billing" tag that
    /// didn't already exist.
    /// </summary>
    Task<Tag?> FindTagByNameAsync(Guid organizationId, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a tag by normalized name within an organization, creating it if it
    /// doesn't exist yet. Backs AddTagAsync, so callers never have to do a
    /// separate "create the tag first" step before tagging a ticket with it.
    /// </summary>
    Task<Tag> GetOrCreateTagAsync(Guid organizationId, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Marks a ticket for hard deletion. EF Core's configured cascade delete
    /// takes its Comments, History, Tags and Attachments with it — see the
    /// doc comment on TicketService.DeleteAsync for why that's a deliberate,
    /// narrow tradeoff rather than an oversight.
    /// </summary>
    void Remove(Ticket ticket);
}
