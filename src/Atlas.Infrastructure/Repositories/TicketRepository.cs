using Atlas.Application.Common.Interfaces;
using Atlas.Application.Tickets.Dtos;
using Atlas.Domain.Entities;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="ITicketRepository"/> against Azure SQL / SQL Server.</summary>
public sealed class TicketRepository : ITicketRepository
{
    private readonly AtlasDbContext _dbContext;

    public TicketRepository(AtlasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Ticket?> GetByIdAsync(Guid id, bool includeDetails, CancellationToken cancellationToken)
    {
        IQueryable<Ticket> query = _dbContext.Tickets
            .Include(t => t.Organization)
            .Include(t => t.AssignedToUser);

        if (includeDetails)
        {
            query = query
                .Include(t => t.Comments)
                .Include(t => t.History)
                .Include(t => t.Tags).ThenInclude(tt => tt.Tag)
                .Include(t => t.Attachments);
        }

        return await query.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    /// <summary>
    /// Backs GET /api/tickets/stats (Del 13). Three small aggregate queries —
    /// status counts, priority counts, an overdue count — rather than one
    /// query that pulls every one of the organization's tickets into memory
    /// and counts them in C#: GROUP BY/COUNT(*) runs server-side in Azure
    /// SQL, so this costs roughly the same whether the organization has 50
    /// tickets or 50,000. Nothing here materializes a Ticket entity at all
    /// (every query below projects straight to an anonymous type), so there's
    /// no change-tracking overhead to opt out of with AsNoTracking either —
    /// there's simply nothing for the change tracker to track.
    /// </summary>
    public async Task<TicketStatsDto> GetStatsAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var baseQuery = _dbContext.Tickets.Where(t => t.OrganizationId == organizationId);

        var statusCounts = await baseQuery
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, cancellationToken);

        var priorityCounts = await baseQuery
            .GroupBy(t => t.Priority)
            .Select(g => new { Priority = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Priority, g => g.Count, cancellationToken);

        // Same overdue definition as GetOverdueAsync below and Ticket.IsOverdue
        // itself, minus GetOverdueAsync's extra "AssignedToUserId != null"
        // filter: that filter exists there because the background worker only
        // cares about tickets it can actually notify someone about, while a
        // dashboard count should reflect the raw fact "N tickets are overdue",
        // assigned or not.
        var now = DateTime.UtcNow;
        var overdueCount = await baseQuery
            .Where(t => t.DueAtUtc != null
                && t.DueAtUtc < now
                && t.Status != Domain.Enums.TicketStatus.Resolved
                && t.Status != Domain.Enums.TicketStatus.Closed
                && t.Status != Domain.Enums.TicketStatus.Cancelled)
            .CountAsync(cancellationToken);

        int StatusCount(Domain.Enums.TicketStatus status) => statusCounts.TryGetValue(status, out var count) ? count : 0;
        int PriorityCount(Domain.Enums.TicketPriority priority) => priorityCounts.TryGetValue(priority, out var count) ? count : 0;

        return new TicketStatsDto(
            OrganizationId: organizationId,
            TotalCount: statusCounts.Values.Sum(),
            NewCount: StatusCount(Domain.Enums.TicketStatus.New),
            OpenCount: StatusCount(Domain.Enums.TicketStatus.Open),
            InProgressCount: StatusCount(Domain.Enums.TicketStatus.InProgress),
            OnHoldCount: StatusCount(Domain.Enums.TicketStatus.OnHold),
            ResolvedCount: StatusCount(Domain.Enums.TicketStatus.Resolved),
            ClosedCount: StatusCount(Domain.Enums.TicketStatus.Closed),
            CancelledCount: StatusCount(Domain.Enums.TicketStatus.Cancelled),
            LowPriorityCount: PriorityCount(Domain.Enums.TicketPriority.Low),
            MediumPriorityCount: PriorityCount(Domain.Enums.TicketPriority.Medium),
            HighPriorityCount: PriorityCount(Domain.Enums.TicketPriority.High),
            CriticalPriorityCount: PriorityCount(Domain.Enums.TicketPriority.Critical),
            OverdueCount: overdueCount,
            GeneratedAtUtc: now);
    }

    public async Task<(IReadOnlyList<Ticket> Items, int TotalCount)> SearchAsync(TicketListQuery query, CancellationToken cancellationToken)
    {
        IQueryable<Ticket> filtered = _dbContext.Tickets.AsNoTracking();

        if (query.Status is not null)
        {
            filtered = filtered.Where(t => t.Status == query.Status);
        }

        if (query.Priority is not null)
        {
            filtered = filtered.Where(t => t.Priority == query.Priority);
        }

        if (query.AssignedToUserId is not null)
        {
            filtered = filtered.Where(t => t.AssignedToUserId == query.AssignedToUserId);
        }

        if (query.OrganizationId is not null)
        {
            filtered = filtered.Where(t => t.OrganizationId == query.OrganizationId);
        }

        if (query.ProjectId is not null)
        {
            filtered = filtered.Where(t => t.ProjectId == query.ProjectId);
        }

        if (query.OverdueOnly == true)
        {
            var now = DateTime.UtcNow;
            filtered = filtered.Where(t =>
                t.DueAtUtc != null
                && t.DueAtUtc < now
                && t.Status != Domain.Enums.TicketStatus.Resolved
                && t.Status != Domain.Enums.TicketStatus.Closed
                && t.Status != Domain.Enums.TicketStatus.Cancelled);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim()}%";
            filtered = filtered.Where(t => EF.Functions.Like(t.Title, pattern));
        }

        var totalCount = await filtered.CountAsync(cancellationToken);

        var items = await filtered
            .Include(t => t.Organization)
            .Include(t => t.AssignedToUser)
            .OrderByDescending(t => t.CreatedAtUtc)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<Ticket>> GetOverdueAsync(CancellationToken cancellationToken)
    {
        // Same overdue definition as Ticket.IsOverdue (the domain property)
        // and SearchAsync's OverdueOnly filter — a due date in the past that
        // hasn't been closed out yet. Re-expressed as a query here (rather
        // than loading every ticket and filtering in memory against
        // IsOverdue) because that's the whole point of a database: this runs
        // as a single indexed query, not "download the table".
        var now = DateTime.UtcNow;

        return await _dbContext.Tickets
            .AsNoTracking()
            .Where(t => t.DueAtUtc != null
                && t.DueAtUtc < now
                && t.AssignedToUserId != null
                && t.Status != Domain.Enums.TicketStatus.Resolved
                && t.Status != Domain.Enums.TicketStatus.Closed
                && t.Status != Domain.Enums.TicketStatus.Cancelled)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        await _dbContext.Tickets.AddAsync(ticket, cancellationToken);
    }

    public async Task AddAttachmentAsync(Attachment attachment, CancellationToken cancellationToken)
    {
        // See the doc comment on ITicketRepository.AddAttachmentAsync: this is
        // an explicit Add() specifically because leaving Attachment to be
        // discovered implicitly (the way TicketComment/TicketHistory/TicketTag
        // are) was producing a DbUpdateConcurrencyException. AddAsync (not the
        // synchronous Add) purely to match the style already used for Ticket
        // and Tag above — Attachment's Guid key is already set by the time we
        // get here, so there's no actual database round-trip happening either way.
        await _dbContext.Attachments.AddAsync(attachment, cancellationToken);
    }

    public void Update(Ticket ticket)
    {
        // Ticket was loaded from this same DbContext, so EF Core's change tracker
        // already knows about every mutation made through its domain methods.
        // Present for interface clarity and to support alternate implementations.
    }

    public async Task<Tag?> FindTagByNameAsync(Guid organizationId, string name, CancellationToken cancellationToken)
    {
        // Normalize the same way Tag.Create does, so "Billing", " billing " and
        // "billing" all resolve to the same row instead of silently missing it.
        var normalized = name.Trim().ToLowerInvariant();

        return await _dbContext.Tags
            .FirstOrDefaultAsync(t => t.OrganizationId == organizationId && t.Name == normalized, cancellationToken);
    }

    public async Task<Tag> GetOrCreateTagAsync(Guid organizationId, string name, CancellationToken cancellationToken)
    {
        var existing = await FindTagByNameAsync(organizationId, name, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var tag = Tag.Create(organizationId, name);
        await _dbContext.Tags.AddAsync(tag, cancellationToken);
        return tag;
    }

    public void Remove(Ticket ticket)
    {
        _dbContext.Tickets.Remove(ticket);
    }
}
