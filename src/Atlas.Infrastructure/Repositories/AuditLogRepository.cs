using Atlas.Application.Audit.Dtos;
using Atlas.Application.Common.Interfaces;
using Atlas.Domain.Entities;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IAuditLogRepository"/> against Azure SQL / SQL Server.</summary>
public sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly AtlasDbContext _dbContext;

    public AuditLogRepository(AtlasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken)
    {
        await _dbContext.AuditLogs.AddAsync(auditLog, cancellationToken);
    }

    public async Task<(IReadOnlyList<(AuditLog AuditLog, string? ActorFullName)> Items, int TotalCount)> SearchAsync(AuditLogListQuery query, CancellationToken cancellationToken)
    {
        IQueryable<AuditLog> filtered = _dbContext.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.EntityName))
        {
            filtered = filtered.Where(a => a.EntityName == query.EntityName);
        }

        if (query.EntityId is not null)
        {
            filtered = filtered.Where(a => a.EntityId == query.EntityId);
        }

        if (query.UserId is not null)
        {
            filtered = filtered.Where(a => a.UserId == query.UserId);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            filtered = filtered.Where(a => a.Action == query.Action);
        }

        if (query.FromUtc is not null)
        {
            filtered = filtered.Where(a => a.TimestampUtc >= query.FromUtc);
        }

        if (query.ToUtc is not null)
        {
            filtered = filtered.Where(a => a.TimestampUtc <= query.ToUtc);
        }

        var totalCount = await filtered.CountAsync(cancellationToken);

        // A left join against Users, not an .Include — AuditLog has no User
        // navigation property (see AuditLogDto's doc comment for why), so this
        // is the query-time equivalent, and it's a *left* join specifically
        // because the acting user could theoretically be deleted later while
        // their audit trail must still be readable (Users has no hard-delete
        // path today, but the join is written to survive it if that ever
        // changes, rather than silently dropping orphaned audit rows).
        var items = await filtered
            .OrderByDescending(a => a.TimestampUtc)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .GroupJoin(_dbContext.Users, a => a.UserId, u => u.Id, (a, users) => new { AuditLog = a, Users = users })
            .SelectMany(x => x.Users.DefaultIfEmpty(), (x, u) => new { x.AuditLog, ActorFullName = u != null ? u.FullName : null })
            .ToListAsync(cancellationToken);

        return (items.Select(i => (i.AuditLog, i.ActorFullName)).ToList(), totalCount);
    }
}
