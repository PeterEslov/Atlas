using Atlas.Domain.Enums;

namespace Atlas.Domain.Security;

/// <summary>
/// The single source of truth for which <see cref="Permissions"/> each
/// <see cref="UserRole"/> is granted:
///
///   Customer -> Ticket.Read, Ticket.Create
///   Agent    -> Customer + Ticket.Update, Ticket.Assign
///   Manager  -> Agent + Ticket.Delete, Project.Read, Project.Manage,
///               Team.Read, Team.Manage, User.Read, User.Manage,
///               Organization.Read
///   Admin    -> everything, including Organization.Manage and AuditLog.Read
///
/// Organization.Manage (create/rename/deactivate an organization — i.e. adding
/// or removing an entire tenant from the system) is deliberately Admin-only:
/// it is a different order of operation from managing the people or projects
/// *within* an organization a Manager already belongs to. User.Manage (change
/// role, deactivate/reactivate a person) is granted to Manager as well as
/// Admin, on the assumption a Manager runs their own team day-to-day —
/// tighten this to Admin-only later if that assumption turns out wrong.
/// Project.Manage and Team.Manage (Del 7) get the same Manager-level trust as
/// User.Manage, not the Organization.Manage treatment: creating a project or
/// an internal team is routine day-to-day work *inside* an organization a
/// Manager already belongs to, not a tenant-boundary change, so there is no
/// reason to reserve it for Admin the way Organization.Manage is.
///
/// AuditLog.Read (Del 15) is Admin-only, and deliberately NOT extended to
/// Manager the way User.Manage/Project.Manage were: a Manager doing their own
/// team's day-to-day work is one thing, but the audit trail also records
/// things a Manager themselves did (e.g. a role change) plus other Managers'
/// and Admins' actions across every organization — there's no per-organization
/// filter on AuditLogs (see AuditLog's own doc comment and the "Known
/// simplifications" note in docs/ARCHITECTURE.md), so granting it below Admin
/// would mean any Manager can already see every tenant's audit history, not
/// just their own — a materially bigger leak than Organization.Read causes
/// today for the same reason.
///
/// Looked up once at login time and baked into the JWT as "permission" claims
/// (see Atlas.Infrastructure's JwtTokenGenerator) — a role change takes effect
/// the next time the user signs in, not mid-session.
/// </summary>
public static class RolePermissions
{
    private static readonly IReadOnlyDictionary<UserRole, IReadOnlySet<string>> Map = new Dictionary<UserRole, IReadOnlySet<string>>
    {
        [UserRole.Customer] = new HashSet<string>
        {
            Permissions.TicketRead,
            Permissions.TicketCreate
        },
        [UserRole.Agent] = new HashSet<string>
        {
            Permissions.TicketRead,
            Permissions.TicketCreate,
            Permissions.TicketUpdate,
            Permissions.TicketAssign
        },
        [UserRole.Manager] = new HashSet<string>
        {
            Permissions.TicketRead,
            Permissions.TicketCreate,
            Permissions.TicketUpdate,
            Permissions.TicketAssign,
            Permissions.TicketDelete,
            Permissions.ProjectRead,
            Permissions.ProjectManage,
            Permissions.TeamRead,
            Permissions.TeamManage,
            Permissions.UserRead,
            Permissions.UserManage,
            Permissions.OrganizationRead
        },
        [UserRole.Admin] = new HashSet<string>(Permissions.All)
    };

    public static IReadOnlySet<string> For(UserRole role) => Map[role];
}
