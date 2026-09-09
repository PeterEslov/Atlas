using Atlas.Domain.Enums;

namespace Atlas.Application.Tickets.Dtos;

/// <summary>Summary shape returned by list/search endpoints — cheap to project, no comments/history.</summary>
public sealed record TicketDto(
    Guid Id,
    string Title,
    TicketStatus Status,
    TicketPriority Priority,
    Guid OrganizationId,
    string OrganizationName,
    Guid? AssignedToUserId,
    string? AssignedToUserName,
    DateTime? DueAtUtc,
    bool IsOverdue,
    DateTime CreatedAtUtc);

/// <summary>Full shape returned by the "get one" endpoint — includes comments, history and tags.</summary>
public sealed record TicketDetailDto(
    Guid Id,
    string Title,
    string Description,
    TicketStatus Status,
    TicketPriority Priority,
    Guid OrganizationId,
    string OrganizationName,
    Guid? ProjectId,
    Guid CreatedByUserId,
    Guid? AssignedToUserId,
    DateTime? DueAtUtc,
    DateTime? ResolvedAtUtc,
    DateTime? ClosedAtUtc,
    bool IsOverdue,
    DateTime CreatedAtUtc,
    DateTime? ModifiedAtUtc,
    IReadOnlyList<TicketCommentDto> Comments,
    IReadOnlyList<TicketHistoryDto> History,
    IReadOnlyList<string> Tags);

public sealed record TicketCommentDto(Guid Id, Guid AuthorUserId, string Body, bool IsInternal, DateTime CreatedAtUtc);

public sealed record TicketHistoryDto(Guid Id, Guid ChangedByUserId, string FieldName, string? OldValue, string? NewValue, DateTime ChangedAtUtc);

/// <summary>Request body for POST /api/tickets.</summary>
public sealed record CreateTicketRequest(
    string Title,
    string Description,
    Guid OrganizationId,
    TicketPriority Priority,
    Guid? ProjectId,
    DateTime? DueAtUtc);

public sealed record AssignTicketRequest(Guid UserId);

public sealed record ChangeTicketStatusRequest(TicketStatus Status);

public sealed record ChangeTicketPriorityRequest(TicketPriority Priority, string? EscalationReason);

public sealed record AddTicketCommentRequest(string Body, bool IsInternal);

/// <summary>
/// Request body for POST /api/tickets/{id}/tags. Takes a tag name, not a tag
/// id — there's no separate "create a tag first, then reference its id" step
/// for the caller. TicketService.AddTagAsync resolves this to a Tag row via
/// get-or-create, scoped to the ticket's own organization.
/// </summary>
public sealed record AddTicketTagRequest(string Name);

/// <summary>Request body for POST /api/tickets/{id}/tags/remove — same name-based shape as AddTicketTagRequest.</summary>
public sealed record RemoveTicketTagRequest(string Name);

/// <summary>
/// Filter/sort/paging parameters for GET /api/tickets, mirroring
/// GET /api/tickets?status=open&amp;priority=high&amp;assignedTo=...&amp;projectId=...&amp;page=2&amp;pageSize=25
/// </summary>
public sealed class TicketListQuery
{
    public TicketStatus? Status { get; init; }
    public TicketPriority? Priority { get; init; }
    public Guid? AssignedToUserId { get; init; }
    public Guid? OrganizationId { get; init; }
    public Guid? ProjectId { get; init; }
    public bool? OverdueOnly { get; init; }
    public string? Search { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;

    public const int MaxPageSize = 100;
}
