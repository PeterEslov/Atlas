using System.Text.Json;

namespace Atlas.Application.Common;

/// <summary>
/// One shared JSON shape for AuditLog.OldValuesJson/NewValuesJson, used by
/// every application service that writes an audit entry (AuthService,
/// UserService, OrganizationService, ProjectService, TicketService) so the
/// column always contains the same style of JSON regardless of which service
/// wrote it — camelCase, matching every API response this project already
/// returns (see the note on JSON casing in docs/ARCHITECTURE.md), rather than
/// each call site picking its own JsonSerializerOptions.
///
/// Deliberately takes `object?` (an anonymous type at each call site, e.g.
/// `new { role = oldRole }`) rather than a typed "AuditValues" class per
/// entity — the whole point of AuditLog storing free-form JSON instead of
/// typed columns is that it can audit any entity without a schema change per
/// entity type (see AuditLog's own doc comment); a typed wrapper here would
/// just re-introduce that same one-shape-per-entity coupling one layer up.
/// </summary>
internal static class AuditLogSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Null in, null out — most audit entries have no "before" (a creation) or no "after" (a hard delete).</summary>
    public static string? ToJson(object? values) => values is null ? null : JsonSerializer.Serialize(values, Options);
}
