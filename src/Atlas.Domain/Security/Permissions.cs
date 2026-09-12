namespace Atlas.Domain.Security;

/// <summary>
/// Fine-grained permission names, distinct from the coarse <see cref="Enums.UserRole"/>
/// enum. A JWT carries the caller's granted permissions as claims (see
/// RolePermissions), and API endpoints authorize against these constants rather
/// than against roles directly — so tightening or loosening what a role can do
/// never requires touching a controller.
/// </summary>
public static class Permissions
{
    public const string TicketRead = "Ticket.Read";
    public const string TicketCreate = "Ticket.Create";
    public const string TicketUpdate = "Ticket.Update";
    public const string TicketAssign = "Ticket.Assign";
    public const string TicketDelete = "Ticket.Delete";

    public const string UserRead = "User.Read";
    public const string UserManage = "User.Manage";

    public const string ProjectRead = "Project.Read";
    public const string ProjectManage = "Project.Manage";

    public const string TeamRead = "Team.Read";
    public const string TeamManage = "Team.Manage";

    public const string OrganizationRead = "Organization.Read";
    public const string OrganizationManage = "Organization.Manage";

    /// <summary>
    /// Read access to the system-wide audit trail (Del 15). Deliberately its
    /// own permission rather than folded into an existing one — the closest
    /// candidate, Organization.Manage, is about *changing* tenant-level data,
    /// while this is about *seeing* a record of who changed what across every
    /// entity, including things a caller with Organization.Manage alone has
    /// no business reading (e.g. another admin's own role changes). See
    /// RolePermissions for why it's granted to Admin only.
    /// </summary>
    public const string AuditLogRead = "AuditLog.Read";

    /// <summary>Every permission that exists — used to grant Admin everything without hand-listing it twice.</summary>
    public static readonly IReadOnlyCollection<string> All =
    [
        TicketRead, TicketCreate, TicketUpdate, TicketAssign, TicketDelete,
        UserRead, UserManage,
        ProjectRead, ProjectManage,
        TeamRead, TeamManage,
        OrganizationRead, OrganizationManage,
        AuditLogRead
    ];
}
