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
}
