using Atlas.Domain.Common;

namespace Atlas.Domain.Entities;

/// <summary>
/// System-wide audit trail, distinct from <see cref="TicketHistory"/> (which only
/// covers ticket field changes). AuditLog captures security- and compliance-relevant
/// events across every entity: user creation, role changes, project archival, etc.
/// OldValues/NewValues are stored as JSON so this single table can audit any entity
/// without a schema change per entity type.
/// </summary>
public sealed class AuditLog : Entity
{
    public Guid UserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string EntityName { get; private set; } = string.Empty;
    public Guid EntityId { get; private set; }
    public string? OldValuesJson { get; private set; }
    public string? NewValuesJson { get; private set; }
    public DateTime TimestampUtc { get; private set; }

    private AuditLog()
    {
        // Required by EF Core for materialization.
    }

    public static AuditLog Create(Guid userId, string action, string entityName, Guid entityId, string? oldValuesJson = null, string? newValuesJson = null) =>
        new()
        {
            UserId = userId,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            OldValuesJson = oldValuesJson,
            NewValuesJson = newValuesJson,
            TimestampUtc = DateTime.UtcNow
        };
}
