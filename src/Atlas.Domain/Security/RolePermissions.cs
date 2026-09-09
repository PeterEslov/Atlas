using Atlas.Domain.Enums;

namespace Atlas.Domain.Security;

/// <summary>
/// The single source of truth for which <see cref="Permissions"/> each
/// <see cref="UserRole"/> is granted:
///
///   Customer -> Ticket.Read, Ticket.Create
///   Agent    -> Customer + Ticket.Update, Ticket.Assign
///   Manager  -> Agent + Ticket.Delete, Project.Read, Project.Manage,
///               User.Read, User.Manage, Organization.Read
///   Admin    -> everything, including Organization.Manage
///
/// Organization.Manage (create/rename/deactivate an organization — i.e. adding
/// or removing an entire tenant from the system) is deliberately Admin-only:
/// it is a different order of operation from managing the people or projects
/// *within* an organization a Manager already belongs to. User.Manage (change
/// role, deactivate/reactivate a person) is granted to Manager as well as
/// Admin, on the assumption a Manager runs their own team day-to-day —
/// tighten this to Admin-only later if that assumption turns out wrong.
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
            Permissions.UserRead,
            Permissions.UserManage,
            Permissions.OrganizationRead
        },
        [UserRole.Admin] = new HashSet<string>(Permissions.All)
    };

    public static IReadOnlySet<string> For(UserRole role) => Map[role];
}
