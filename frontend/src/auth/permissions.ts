// Mirrors src/Atlas.Domain/Security/Permissions.cs exactly — these strings are
// what AuthResponseDto.permissions actually contains (see JwtTokenGenerator,
// which stamps one "permission" claim per string in RolePermissions.For(role)
// onto the token, and AuthController returns that same list back in the
// login/register response body so the frontend never has to decode the JWT
// itself just to know what a user can do).
export const Permissions = {
  TicketRead: "Ticket.Read",
  TicketCreate: "Ticket.Create",
  TicketUpdate: "Ticket.Update",
  TicketAssign: "Ticket.Assign",
  TicketDelete: "Ticket.Delete",

  UserRead: "User.Read",
  UserManage: "User.Manage",

  ProjectRead: "Project.Read",
  ProjectManage: "Project.Manage",

  TeamRead: "Team.Read",
  TeamManage: "Team.Manage",

  OrganizationRead: "Organization.Read",
  OrganizationManage: "Organization.Manage",

  AuditLogRead: "AuditLog.Read",
} as const;

export type Permission = (typeof Permissions)[keyof typeof Permissions];
