namespace Atlas.Domain.Enums;

/// <summary>Lifecycle state of a <see cref="Entities.Ticket"/>.</summary>
public enum TicketStatus
{
    New = 0,
    Open = 1,
    InProgress = 2,
    OnHold = 3,
    Resolved = 4,
    Closed = 5,
    Cancelled = 6
}

/// <summary>Business priority of a ticket. Numeric value doubles as sort order (higher = more urgent).</summary>
public enum TicketPriority
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

/// <summary>Distinguishes an internal organization (e.g. Northstar IT itself) from a customer organization (e.g. ACME AB).</summary>
public enum OrganizationType
{
    Internal = 0,
    Customer = 1
}

/// <summary>Coarse-grained role. Fine-grained permissions are introduced in a later phase (Del 5).</summary>
public enum UserRole
{
    Customer = 0,
    Agent = 1,
    Manager = 2,
    Admin = 3
}

/// <summary>Kind of event recorded in <see cref="Entities.Notification"/>.</summary>
public enum NotificationType
{
    TicketAssigned = 0,
    TicketPriorityChanged = 1,
    TicketStatusChanged = 2,
    CommentAdded = 3,
    TicketOverdue = 4
}
