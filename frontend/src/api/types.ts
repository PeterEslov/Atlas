// Hand-mirrored from the C# DTOs in src/Atlas.Application/*/Dtos and
// src/Atlas.Domain/Enums (no OpenAPI/NSwag code-gen step exists yet — a
// reasonable Del 22 candidate if this drifts enough to hurt). Property names
// are camelCase because Atlas.Api's AddControllers() uses ASP.NET Core's web
// defaults (JsonNamingPolicy.CamelCase) with no further JsonOptions
// configuration — see Program.cs.
//
// Enum *numbers*, not strings: Program.cs never registers a
// JsonStringEnumConverter, so System.Text.Json falls back to its default —
// enums serialize as their underlying int, both ways. A request body that
// sends `"status": "Open"` instead of `"status": 1` fails model binding
// silently different from what Swagger's dropdown suggests, so every enum
// below is a numeric TypeScript enum with values copied 1:1 from the C# side,
// and every <select> in the UI must submit the numeric value, not the label.

export enum TicketStatus {
  New = 0,
  Open = 1,
  InProgress = 2,
  OnHold = 3,
  Resolved = 4,
  Closed = 5,
  Cancelled = 6,
}

export enum TicketPriority {
  Low = 0,
  Medium = 1,
  High = 2,
  Critical = 3,
}

export enum OrganizationType {
  Internal = 0,
  Customer = 1,
}

export enum UserRole {
  Customer = 0,
  Agent = 1,
  Manager = 2,
  Admin = 3,
}

export const TicketStatusLabels: Record<TicketStatus, string> = {
  [TicketStatus.New]: "Ny",
  [TicketStatus.Open]: "Öppen",
  [TicketStatus.InProgress]: "Pågår",
  [TicketStatus.OnHold]: "Väntar",
  [TicketStatus.Resolved]: "Löst",
  [TicketStatus.Closed]: "Stängd",
  [TicketStatus.Cancelled]: "Avbruten",
};

export const TicketPriorityLabels: Record<TicketPriority, string> = {
  [TicketPriority.Low]: "Låg",
  [TicketPriority.Medium]: "Medel",
  [TicketPriority.High]: "Hög",
  [TicketPriority.Critical]: "Kritisk",
};

export const OrganizationTypeLabels: Record<OrganizationType, string> = {
  [OrganizationType.Internal]: "Intern",
  [OrganizationType.Customer]: "Kund",
};

export const UserRoleLabels: Record<UserRole, string> = {
  [UserRole.Customer]: "Customer",
  [UserRole.Agent]: "Agent",
  [UserRole.Manager]: "Manager",
  [UserRole.Admin]: "Admin",
};

/** Turns a TS numeric enum object into [value,label][] pairs for a <select>. */
export function enumOptions<T extends number>(
  values: Record<T, string>,
): Array<{ value: T; label: string }> {
  return (Object.keys(values) as unknown as T[])
    .filter((k) => !Number.isNaN(Number(k)))
    .map((value) => ({ value, label: values[value] }));
}

// ---- Common ---------------------------------------------------------------

/** Mirrors Atlas.Application.Common.Models.PagedResult<T>. */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

/** RFC 7807 shape ExceptionHandlingMiddleware writes for every error response. */
export interface ProblemDetails {
  status?: number;
  title?: string;
  detail?: string;
  instance?: string;
}

// ---- Auth (Del 5) -----------------------------------------------------------

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  fullName: string;
  email: string;
  password: string;
  organizationId: string;
  role: UserRole;
}

export interface AuthResponseDto {
  token: string;
  expiresAtUtc: string;
  userId: string;
  fullName: string;
  email: string;
  role: UserRole;
  permissions: string[];
}

// ---- Tickets (Del 1-4, 11-13) -----------------------------------------------

export interface TicketDto {
  id: string;
  title: string;
  status: TicketStatus;
  priority: TicketPriority;
  organizationId: string;
  organizationName: string;
  assignedToUserId: string | null;
  assignedToUserName: string | null;
  dueAtUtc: string | null;
  isOverdue: boolean;
  createdAtUtc: string;
}

export interface TicketCommentDto {
  id: string;
  authorUserId: string;
  body: string;
  isInternal: boolean;
  createdAtUtc: string;
}

export interface TicketHistoryDto {
  id: string;
  changedByUserId: string;
  fieldName: string;
  oldValue: string | null;
  newValue: string | null;
  changedAtUtc: string;
}

export interface AttachmentDto {
  id: string;
  fileName: string;
  contentType: string;
  sizeInBytes: number;
  uploadedByUserId: string;
  createdAtUtc: string;
}

export interface TicketDetailDto {
  id: string;
  title: string;
  description: string;
  status: TicketStatus;
  priority: TicketPriority;
  organizationId: string;
  organizationName: string;
  projectId: string | null;
  createdByUserId: string;
  assignedToUserId: string | null;
  dueAtUtc: string | null;
  resolvedAtUtc: string | null;
  closedAtUtc: string | null;
  isOverdue: boolean;
  createdAtUtc: string;
  modifiedAtUtc: string | null;
  comments: TicketCommentDto[];
  history: TicketHistoryDto[];
  tags: string[];
  attachments: AttachmentDto[];
}

export interface CreateTicketRequest {
  title: string;
  description: string;
  organizationId: string;
  priority: TicketPriority;
  projectId: string | null;
  dueAtUtc: string | null;
}

export interface AssignTicketRequest {
  userId: string;
}

export interface ChangeTicketStatusRequest {
  status: TicketStatus;
}

export interface ChangeTicketPriorityRequest {
  priority: TicketPriority;
  escalationReason: string | null;
}

export interface AddTicketCommentRequest {
  body: string;
  isInternal: boolean;
}

export interface AddTicketTagRequest {
  name: string;
}

export interface RemoveTicketTagRequest {
  name: string;
}

export interface TicketListQuery {
  status?: TicketStatus;
  priority?: TicketPriority;
  assignedToUserId?: string;
  organizationId?: string;
  projectId?: string;
  overdueOnly?: boolean;
  search?: string;
  page?: number;
  pageSize?: number;
}

export interface TicketStatsDto {
  organizationId: string;
  totalCount: number;
  newCount: number;
  openCount: number;
  inProgressCount: number;
  onHoldCount: number;
  resolvedCount: number;
  closedCount: number;
  cancelledCount: number;
  lowPriorityCount: number;
  mediumPriorityCount: number;
  highPriorityCount: number;
  criticalPriorityCount: number;
  overdueCount: number;
  generatedAtUtc: string;
}

// ---- Organizations (Del 6) ---------------------------------------------------

export interface OrganizationDto {
  id: string;
  name: string;
  type: OrganizationType;
  isActive: boolean;
  createdAtUtc: string;
}

export interface OrganizationDetailDto {
  id: string;
  name: string;
  type: OrganizationType;
  isActive: boolean;
  userCount: number;
  teamCount: number;
  projectCount: number;
  createdAtUtc: string;
  modifiedAtUtc: string | null;
}

export interface CreateOrganizationRequest {
  name: string;
  type: OrganizationType;
}

export interface RenameOrganizationRequest {
  name: string;
}

export interface OrganizationListQuery {
  type?: OrganizationType;
  isActive?: boolean;
  search?: string;
  page?: number;
  pageSize?: number;
}

// ---- Users (Del 6) ------------------------------------------------------------

export interface UserDto {
  id: string;
  fullName: string;
  email: string;
  role: UserRole;
  organizationId: string;
  organizationName: string;
  isActive: boolean;
  createdAtUtc: string;
}

export interface ChangeUserRoleRequest {
  role: UserRole;
}

export interface UserListQuery {
  organizationId?: string;
  role?: UserRole;
  isActive?: boolean;
  search?: string;
  page?: number;
  pageSize?: number;
}

// ---- Projects (Del 7) -----------------------------------------------------

export interface ProjectDto {
  id: string;
  organizationId: string;
  name: string;
  description: string;
  isArchived: boolean;
  memberCount: number;
  createdAtUtc: string;
}

export interface ProjectMemberDto {
  userId: string;
  fullName: string;
  joinedAtUtc: string;
}

export interface ProjectDetailDto {
  id: string;
  organizationId: string;
  name: string;
  description: string;
  isArchived: boolean;
  members: ProjectMemberDto[];
  createdAtUtc: string;
  modifiedAtUtc: string | null;
}

export interface CreateProjectRequest {
  organizationId: string;
  name: string;
  description?: string;
}

export interface AddProjectMemberRequest {
  userId: string;
}

export interface ProjectListQuery {
  organizationId?: string;
  isArchived?: boolean;
  search?: string;
  page?: number;
  pageSize?: number;
}

// ---- Teams (Del 7) ----------------------------------------------------------

export interface TeamDto {
  id: string;
  organizationId: string;
  name: string;
  memberCount: number;
  createdAtUtc: string;
}

export interface TeamMemberDto {
  userId: string;
  fullName: string;
  joinedAtUtc: string;
}

export interface TeamDetailDto {
  id: string;
  organizationId: string;
  name: string;
  members: TeamMemberDto[];
  createdAtUtc: string;
  modifiedAtUtc: string | null;
}

export interface CreateTeamRequest {
  organizationId: string;
  name: string;
}

export interface AddTeamMemberRequest {
  userId: string;
}

export interface TeamListQuery {
  organizationId?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}

// ---- Audit log (Del 15) -------------------------------------------------------

export interface AuditLogDto {
  id: string;
  userId: string;
  actorFullName: string | null;
  action: string;
  entityName: string;
  entityId: string;
  oldValuesJson: string | null;
  newValuesJson: string | null;
  timestampUtc: string;
}

export interface AuditLogListQuery {
  entityName?: string;
  entityId?: string;
  userId?: string;
  action?: string;
  fromUtc?: string;
  toUtc?: string;
  page?: number;
  pageSize?: number;
}
