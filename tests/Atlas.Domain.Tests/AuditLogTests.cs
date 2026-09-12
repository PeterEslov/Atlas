using Atlas.Domain.Entities;
using Xunit;

namespace Atlas.Domain.Tests;

public class AuditLogTests
{
    [Fact]
    public void Create_SetsEveryField_AndStampsTimestampUtc()
    {
        var actorUserId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var before = DateTime.UtcNow;

        var auditLog = AuditLog.Create(actorUserId, "RoleChanged", nameof(User), entityId, "{\"role\":\"Agent\"}", "{\"role\":\"Manager\"}");

        var after = DateTime.UtcNow;

        Assert.Equal(actorUserId, auditLog.UserId);
        Assert.Equal("RoleChanged", auditLog.Action);
        Assert.Equal(nameof(User), auditLog.EntityName);
        Assert.Equal(entityId, auditLog.EntityId);
        Assert.Equal("{\"role\":\"Agent\"}", auditLog.OldValuesJson);
        Assert.Equal("{\"role\":\"Manager\"}", auditLog.NewValuesJson);

        // Not asserted against DateTime.UtcNow a second time — captured once
        // before and once after the call, so this can never be flaky under
        // slow CI the way asserting against a freshly-read "now" could be.
        Assert.InRange(auditLog.TimestampUtc, before, after);
    }

    [Fact]
    public void Create_WithNoOldValues_LeavesOldValuesJsonNull()
    {
        // The shape a "Created" audit entry uses (AuthService.RegisterAsync,
        // OrganizationService.CreateAsync, ProjectService.CreateAsync,
        // Del 15) — there is no "before" state for something that didn't
        // exist yet.
        var auditLog = AuditLog.Create(Guid.NewGuid(), "Created", nameof(Organization), Guid.NewGuid(), newValuesJson: "{\"name\":\"Acme\"}");

        Assert.Null(auditLog.OldValuesJson);
        Assert.Equal("{\"name\":\"Acme\"}", auditLog.NewValuesJson);
    }

    [Fact]
    public void Create_WithNoNewValues_LeavesNewValuesJsonNull()
    {
        // The shape TicketService.DeleteAsync uses (Del 15) — there is no
        // "after" state once the entity has been hard-deleted, only a
        // snapshot of what it looked like right before.
        var auditLog = AuditLog.Create(Guid.NewGuid(), "Deleted", nameof(Ticket), Guid.NewGuid(), oldValuesJson: "{\"title\":\"Printer jam\"}");

        Assert.Equal("{\"title\":\"Printer jam\"}", auditLog.OldValuesJson);
        Assert.Null(auditLog.NewValuesJson);
    }
}
