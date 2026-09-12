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

    /// <summary>
    /// Aggregate ticket counts for one organization — status counts, priority
    /// counts, and how many are currently overdue (Del 13). Computed with
    /// GROUP BY/COUNT queries directly in SQL (see TicketRepository), never
    /// by loading every ticket into memory and counting in C# — exactly the
    /// expensive-if-repeated query this Del's Redis cache exists to avoid
    /// re-running on every dashboard refresh.
    /// </summary>
    Task<TicketStatsDto> GetStatsAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>
    /// Every ticket that is currently overdue (a past due date, not yet
    /// Resolved/Closed/Cancelled) AND assigned to someone — an unassigned
    /// ticket has nobody to notify, so it's filtered out here rather than by
    /// the caller. Used by Del 11's background worker; deliberately not
    /// paginated like SearchAsync is, since a batch job needs the complete
    /// set every time, not a UI page of it.
    /// </summary>
    Task<IReadOnlyList<Ticket>> GetOverdueAsync(CancellationToken cancellationToken);

    Task AddAsync(Ticket ticket, CancellationToken cancellationToken);

    /// <summary>
    /// Explicitly stages a brand-new Attachment as EntityState.Added.
    ///
    /// Every other child entity (TicketComment, TicketHistory, TicketTag) is
    /// added purely by appending it to the Ticket's own in-memory collection
    /// — e.g. ticket.AddComment(...) does "_comments.Add(comment)" — and
    /// relying on EF Core's change tracker to discover it on its own via
    /// DetectChanges(). No repository method is needed for those.
    ///
    /// Attachment goes through this explicit Add() instead, because leaving
    /// it to that same implicit discovery was deterministically producing an
    /// UPDATE (0 rows affected → DbUpdateConcurrencyException) instead of an
    /// INSERT. EF Core's own docs call out exactly this ambiguity for entities
    /// with client-generated (not database-generated) keys — which is what we
    /// have here, since Attachment.Id is a Guid assigned in the constructor,
    /// before EF ever sees it: without an explicit Add(), EF has to *guess*
    /// whether a newly-discovered entity is brand new (Added) or an existing
    /// row being re-attached (Unchanged/Modified). That guess is what went
    /// wrong for this one call path (see TicketService.AddAttachmentAsync for
    /// the full story — it's the only method that awaits something external,
    /// the Blob Storage upload, between loading the tracked ticket and saving,
    /// which is the one structural difference from AddComment/AddTag/etc.).
    /// Calling this removes the guesswork entirely rather than relying on it
    /// being resolved correctly by chance.
    /// </summary>
    Task AddAttachmentAsync(Attachment attachment, CancellationToken cancellationToken);

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
