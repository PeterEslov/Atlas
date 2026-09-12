using Atlas.Application.Audit.Dtos;
using Atlas.Application.Audit.Services;
using Atlas.Application.Common.Models;
using Atlas.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Api.Controllers;

/// <summary>
/// Audit trail endpoints (Del 15). Read-only — there is no POST here, since
/// every audit entry is a side effect of some other endpoint's own use case
/// (see IAuditLogService's doc comment). Gated behind AuditLog.Read, granted
/// to Admin only — see the doc comment on RolePermissions for why this is
/// deliberately narrower than the Manager-level trust User.Manage/
/// Project.Manage get.
/// </summary>
[ApiController]
[Route("api/audit-logs")]
[Produces("application/json")]
[Authorize]
public sealed class AuditLogsController : ControllerBase
{
    private readonly IAuditLogService _auditLogService;

    public AuditLogsController(IAuditLogService auditLogService)
    {
        _auditLogService = auditLogService;
    }

    /// <summary>
    /// GET /api/audit-logs?entityName=Ticket&amp;entityId={guid}&amp;userId={guid}&amp;action=Deleted&amp;fromUtc=2026-01-01T00:00:00Z&amp;toUtc=2026-12-31T23:59:59Z&amp;page=1&amp;pageSize=25
    ///
    /// Newest first (see AuditLogRepository.SearchAsync) — an audit trail is
    /// read "what just happened" far more often than "what happened first".
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Permissions.AuditLogRead)]
    [ProducesResponseType(typeof(PagedResult<AuditLogDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AuditLogDto>>> Search([FromQuery] AuditLogListQuery query, CancellationToken cancellationToken)
    {
        var result = await _auditLogService.SearchAsync(query, cancellationToken);
        return Ok(result);
    }
}
