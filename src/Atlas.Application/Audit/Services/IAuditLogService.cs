using Atlas.Application.Audit.Dtos;
using Atlas.Application.Common.Models;

namespace Atlas.Application.Audit.Services;

/// <summary>
/// Read-only on purpose: writing an audit entry always happens as a side
/// effect of some other use case (a role change, an org rename, a ticket
/// delete, ...) inside that use case's own service, directly against
/// IAuditLogRepository — see the doc comment there for why. There is no
/// "record an audit entry" use case of its own, so there's nothing for a
/// write method here to do.
/// </summary>
public interface IAuditLogService
{
    Task<PagedResult<AuditLogDto>> SearchAsync(AuditLogListQuery query, CancellationToken cancellationToken);
}
