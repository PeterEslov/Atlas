using Atlas.Application.Audit.Dtos;
using Atlas.Domain.Entities;

namespace Atlas.Application.Common.Interfaces;

/// <summary>
/// Persistence contract for <see cref="AuditLog"/>. Narrow, like every other
/// repository in this project (see ITicketRepository's own doc comment for
/// why there's no generic IRepository&lt;T&gt;) — just "add one" and "search",
/// nothing entity-specific, because AuditLog itself is deliberately generic
/// (one table, any entity — see the doc comment on the entity).
///
/// AddAsync is called from *inside* AuthService/UserService/OrganizationService/
/// ProjectService/TicketService, before those services' own
/// IUnitOfWork.SaveChangesAsync — never followed by a SaveChangesAsync of its
/// own. That's deliberate: it stages the AuditLog row on the same scoped
/// AtlasDbContext as the domain change being audited, so one SaveChangesAsync
/// call commits both in the same transaction. Unlike Del 13's Redis cache
/// (an optional dependency that must fail open — see ITicketStatsCache), an
/// audit record is a compliance artifact backed by the *same* database as
/// the change it describes, so there's no "fail open" story here: if the
/// SaveChangesAsync that would have persisted the audited change also fails
/// to persist its audit row, the whole transaction rolls back and the
/// audited change never happened either — exactly the atomicity a compliance
/// record needs, for free, just by sharing the DbContext instead of writing
/// the audit row via a separate call.
/// </summary>
public interface IAuditLogRepository
{
    Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken);

    Task<(IReadOnlyList<(AuditLog AuditLog, string? ActorFullName)> Items, int TotalCount)> SearchAsync(AuditLogListQuery query, CancellationToken cancellationToken);
}
