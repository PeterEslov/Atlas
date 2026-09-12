namespace Atlas.Application.Audit.Dtos;

/// <summary>
/// One row of the audit trail. ActorFullName is resolved with a join against
/// Users at read time (see AuditLogRepository.SearchAsync) rather than by
/// giving AuditLog itself a User navigation property — the entity has stood
/// unchanged since Fas 1, and a GUID-only foreign key is enough for every
/// write site; only the read side needs a human-readable name, so that's the
/// only place carrying the extra join. OldValuesJson/NewValuesJson are
/// returned as-is (already valid JSON text, produced by AuditLogSerializer)
/// rather than deserialized into a typed shape — a caller inspecting one
/// entry already knows which EntityName it is and can parse accordingly; a
/// generic audit-log DTO has no business knowing every entity's field shapes.
/// </summary>
public sealed record AuditLogDto(
    Guid Id,
    Guid UserId,
    string? ActorFullName,
    string Action,
    string EntityName,
    Guid EntityId,
    string? OldValuesJson,
    string? NewValuesJson,
    DateTime TimestampUtc);

/// <summary>
/// Filter/sort/paging parameters for GET /api/audit-logs, mirroring
/// GET /api/audit-logs?entityName=Ticket&amp;entityId={guid}&amp;userId={guid}&amp;action=Deleted&amp;fromUtc=...&amp;toUtc=...&amp;page=1&amp;pageSize=25
///
/// EntityName/EntityId are typically used together ("show me everything ever
/// recorded against this one ticket/user/organization/project"), but each
/// works alone too — EntityName alone answers "show me every role change
/// across all users", not just one.
/// </summary>
public sealed class AuditLogListQuery
{
    public string? EntityName { get; init; }
    public Guid? EntityId { get; init; }
    public Guid? UserId { get; init; }
    public string? Action { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;

    public const int MaxPageSize = 100;
}
