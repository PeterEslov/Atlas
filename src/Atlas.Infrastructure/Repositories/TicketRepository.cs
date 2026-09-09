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

    public async Task AddAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        await _dbContext.Tickets.AddAsync(ticket, cancellationToken);
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
