using Atlas.Domain.Enums;

namespace Atlas.Domain.Security;

/// <summary>
/// The single source of truth for which <see cref="Permissions"/> each
/// <see cref="UserRole"/> is granted:
///
///   Customer -> Ticket.Read, Ticket.Create
///   Agent    -> Customer + Ticket.Update, Ticket.Assign
///   Manager  -> Agent + Ticket.Delete, Project.Read, Project.Manage, User.Read
///   Admin    -> everything
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
            Permissions.UserRead
        },
        [UserRole.Admin] = new HashSet<string>(Permissions.All)
    };

    public static IReadOnlySet<string> For(UserRole role) => Map[role];
}
