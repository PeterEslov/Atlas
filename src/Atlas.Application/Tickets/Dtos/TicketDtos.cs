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

/// <summary>Full shape returned by the "get one" endpoint — includes comments, history, tags and attachments.</summary>
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
    IReadOnlyList<string> Tags,
    IReadOnlyList<AttachmentDto> Attachments);

public sealed record TicketCommentDto(Guid Id, Guid AuthorUserId, string Body, bool IsInternal, DateTime CreatedAtUtc);

public sealed record TicketHistoryDto(Guid Id, Guid ChangedByUserId, string FieldName, string? OldValue, string? NewValue, DateTime ChangedAtUtc);

/// <summary>Metadata returned for an uploaded attachment — never the file bytes themselves; see AttachmentDownload for those.</summary>
public sealed record AttachmentDto(Guid Id, string FileName, string ContentType, long SizeInBytes, Guid UploadedByUserId, DateTime CreatedAtUtc);

/// <summary>
/// Returned by ITicketService.DownloadAttachmentAsync — an open, readable stream
/// plus the metadata the controller needs to set the HTTP response headers
/// (Content-Type and the download's suggested file name). The controller is
/// responsible for disposing the stream once it's finished writing it to the
/// response (ASP.NET Core's FileStreamResult does this automatically).
/// </summary>
public sealed record AttachmentDownload(Stream Content, string ContentType, string FileName);

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

/// <summary>
/// Aggregate ticket counts for one organization — returned by
/// GET /api/tickets/stats and the entire reason Del 13 exists: this is the
/// "dashboard query" that gets cached in Redis (see ITicketStatsCache /
/// RedisTicketStatsCache) instead of re-running its GROUP BY queries against
/// Azure SQL on every single request.
///
/// Deliberately flat, explicitly named counts rather than something like
/// Dictionary&lt;TicketStatus,int&gt;: a dashboard, curl, or Swagger reads
/// named fields directly, with no enum-keyed dictionary to unpack — and it
/// sidesteps ever having to think about how System.Text.Json serializes a
/// non-string dictionary key in the first place.
///
/// GeneratedAtUtc is when this snapshot was computed, not when it was
/// returned — on a cache hit, it can be (up to) Redis:StatsCacheTtlSeconds
/// old, which is exactly the trade-off a cache makes on purpose: slightly
/// stale data in exchange for not hitting the database every time. It's
/// included specifically so a caller (or Peter, testing this by hand) can
/// tell a cache hit from a cache miss just by looking at the response.
/// </summary>
public sealed record TicketStatsDto(
    Guid OrganizationId,
    int TotalCount,
    int NewCount,
    int OpenCount,
    int InProgressCount,
    int OnHoldCount,
    int ResolvedCount,
    int ClosedCount,
    int CancelledCount,
    int LowPriorityCount,
    int MediumPriorityCount,
    int HighPriorityCount,
    int CriticalPriorityCount,
    int OverdueCount,
    DateTime GeneratedAtUtc);
