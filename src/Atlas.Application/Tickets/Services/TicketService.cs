using Atlas.Application.Common.Exceptions;
using Atlas.Application.Common.Interfaces;
using Atlas.Application.Common.Models;
using Atlas.Application.Tickets.Dtos;
using Atlas.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Tickets.Services;

/// <summary>
/// Orchestrates ticket use cases: loads the aggregate via the repository, invokes
/// the domain method that owns the business rule, persists via the unit of work,
/// and maps the result to a DTO. All validation of *state transitions* lives on
/// <see cref="Ticket"/> itself — this class never duplicates that logic.
/// </summary>
public sealed class TicketService : ITicketService
{
    private readonly ITicketRepository _ticketRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<TicketService> _logger;

    public TicketService(ITicketRepository ticketRepository, IUnitOfWork unitOfWork, ILogger<TicketService> logger)
    {
        _ticketRepository = ticketRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<PagedResult<TicketDto>> SearchAsync(TicketListQuery query, CancellationToken cancellationToken)
    {
        var normalizedQuery = Normalize(query);
        var (items, totalCount) = await _ticketRepository.SearchAsync(normalizedQuery, cancellationToken);

        var dtos = items.Select(ToDto).ToList();
        return new PagedResult<TicketDto>(dtos, normalizedQuery.Page, normalizedQuery.PageSize, totalCount);
    }

    public async Task<TicketDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var ticket = await _ticketRepository.GetByIdAsync(id, includeDetails: true, cancellationToken);
        return ticket is null ? null : ToDetailDto(ticket);
    }

    public async Task<TicketDetailDto> CreateAsync(CreateTicketRequest request, Guid createdByUserId, CancellationToken cancellationToken)
    {
        var ticket = Ticket.Create(
            request.Title,
            request.Description,
            request.OrganizationId,
            createdByUserId,
            request.Priority,
            request.ProjectId,
            request.DueAtUtc);

        await _ticketRepository.AddAsync(ticket, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Ticket {TicketId} created by user {UserId}", ticket.Id, createdByUserId);
        return ToDetailDto(ticket);
    }

    public async Task<TicketDetailDto> AssignAsync(Guid ticketId, AssignTicketRequest request, Guid actorUserId, CancellationToken cancellationToken)
    {
        var ticket = await GetTicketOrThrowAsync(ticketId, cancellationToken);

        ticket.AssignTo(request.UserId, actorUserId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Ticket {TicketId} assigned to user {AssignedUserId} by {ActorUserId}", ticketId, request.UserId, actorUserId);
        return ToDetailDto(ticket);
    }

    public async Task<TicketDetailDto> ChangeStatusAsync(Guid ticketId, ChangeTicketStatusRequest request, Guid actorUserId, CancellationToken cancellationToken)
    {
        var ticket = await GetTicketOrThrowAsync(ticketId, cancellationToken);

        ticket.ChangeStatus(request.Status, actorUserId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Ticket {TicketId} status changed to {Status} by {ActorUserId}", ticketId, request.Status, actorUserId);
        return ToDetailDto(ticket);
    }

    public async Task<TicketDetailDto> ChangePriorityAsync(Guid ticketId, ChangeTicketPriorityRequest request, Guid actorUserId, CancellationToken cancellationToken)
    {
        var ticket = await GetTicketOrThrowAsync(ticketId, cancellationToken);

        ticket.ChangePriority(request.Priority, actorUserId, request.EscalationReason);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Ticket {TicketId} priority changed to {Priority} by {ActorUserId}", ticketId, request.Priority, actorUserId);
        return ToDetailDto(ticket);
    }

    public async Task<TicketCommentDto> AddCommentAsync(Guid ticketId, AddTicketCommentRequest request, Guid authorUserId, CancellationToken cancellationToken)
    {
        var ticket = await GetTicketOrThrowAsync(ticketId, cancellationToken);

        var comment = ticket.AddComment(authorUserId, request.Body, request.IsInternal);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Comment added to ticket {TicketId} by {AuthorUserId}", ticketId, authorUserId);
        return new TicketCommentDto(comment.Id, comment.AuthorUserId, comment.Body, comment.IsInternal, comment.CreatedAtUtc);
    }

    public async Task<TicketDetailDto> ReopenAsync(Guid ticketId, Guid actorUserId, CancellationToken cancellationToken)
    {
        var ticket = await GetTicketOrThrowAsync(ticketId, cancellationToken);

        ticket.Reopen(actorUserId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Ticket {TicketId} reopened by {ActorUserId}", ticketId, actorUserId);
        return ToDetailDto(ticket);
    }

    public async Task<TicketDetailDto> AddTagAsync(Guid ticketId, AddTicketTagRequest request, Guid actorUserId, CancellationToken cancellationToken)
    {
        var ticket = await GetTicketOrThrowAsync(ticketId, cancellationToken);

        // Get-or-create means the caller never has to create the Tag row first —
        // tagging a ticket with a name that doesn't exist yet just creates it,
        // scoped to the ticket's own organization.
        var tag = await _ticketRepository.GetOrCreateTagAsync(ticket.OrganizationId, request.Name, cancellationToken);
        ticket.AddTag(tag.Id);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Tag '{TagName}' added to ticket {TicketId} by {ActorUserId}", tag.Name, ticketId, actorUserId);
        return ToDetailDto(ticket);
    }

    public async Task<TicketDetailDto> RemoveTagAsync(Guid ticketId, RemoveTicketTagRequest request, Guid actorUserId, CancellationToken cancellationToken)
    {
        var ticket = await GetTicketOrThrowAsync(ticketId, cancellationToken);

        // Unlike AddTagAsync, this deliberately does NOT get-or-create — removing
        // a tag that was never applied (or was already removed) is a no-op, not
        // a reason to create the tag row just to immediately not-use it.
        var tag = await _ticketRepository.FindTagByNameAsync(ticket.OrganizationId, request.Name, cancellationToken);
        if (tag is not null)
        {
            ticket.RemoveTag(tag.Id);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation("Tag '{TagName}' removed from ticket {TicketId} by {ActorUserId}", request.Name, ticketId, actorUserId);
        return ToDetailDto(ticket);
    }

    /// <summary>
    /// Hard-deletes a ticket. This is deliberately different from soft-cancelling
    /// one via ChangeStatusAsync(Cancelled): a cancelled ticket still exists and
    /// keeps its full TicketHistory audit trail, while this permanently removes
    /// the row and — via the Cascade delete configured in TicketConfigurations —
    /// its Comments, History, Tags and Attachments along with it. That's a real
    /// tradeoff (the audit trail is gone, not just marked closed), which is why
    /// it sits behind its own narrow Permissions.TicketDelete policy rather than
    /// the general TicketUpdate permission that covers status changes.
    /// </summary>
    public async Task DeleteAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        var ticket = await GetTicketOrThrowAsync(ticketId, cancellationToken);

        _ticketRepository.Remove(ticket);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Ticket {TicketId} permanently deleted", ticketId);
    }

    private async Task<Ticket> GetTicketOrThrowAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        var ticket = await _ticketRepository.GetByIdAsync(ticketId, includeDetails: true, cancellationToken);
        if (ticket is null)
        {
            throw new NotFoundException(nameof(Ticket), ticketId);
        }

        return ticket;
    }

    private static TicketListQuery Normalize(TicketListQuery query)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize switch
        {
            < 1 => 25,
            > TicketListQuery.MaxPageSize => TicketListQuery.MaxPageSize,
            _ => query.PageSize
        };

        return new TicketListQuery
        {
            Status = query.Status,
            Priority = query.Priority,
            AssignedToUserId = query.AssignedToUserId,
            OrganizationId = query.OrganizationId,
            ProjectId = query.ProjectId,
            OverdueOnly = query.OverdueOnly,
            Search = query.Search,
            Page = page,
            PageSize = pageSize
        };
    }

    private static TicketDto ToDto(Ticket ticket) => new(
        ticket.Id,
        ticket.Title,
        ticket.Status,
        ticket.Priority,
        ticket.OrganizationId,
        ticket.Organization?.Name ?? string.Empty,
        ticket.AssignedToUserId,
        ticket.AssignedToUser?.FullName,
        ticket.DueAtUtc,
        ticket.IsOverdue,
        ticket.CreatedAtUtc);

    private static TicketDetailDto ToDetailDto(Ticket ticket) => new(
        ticket.Id,
        ticket.Title,
        ticket.Description,
        ticket.Status,
        ticket.Priority,
        ticket.OrganizationId,
        ticket.Organization?.Name ?? string.Empty,
        ticket.ProjectId,
        ticket.CreatedByUserId,
        ticket.AssignedToUserId,
        ticket.DueAtUtc,
        ticket.ResolvedAtUtc,
        ticket.ClosedAtUtc,
        ticket.IsOverdue,
        ticket.CreatedAtUtc,
        ticket.ModifiedAtUtc,
        ticket.Comments.OrderBy(c => c.CreatedAtUtc).Select(c => new TicketCommentDto(c.Id, c.AuthorUserId, c.Body, c.IsInternal, c.CreatedAtUtc)).ToList(),
        ticket.History.OrderBy(h => h.ChangedAtUtc).Select(h => new TicketHistoryDto(h.Id, h.ChangedByUserId, h.FieldName, h.OldValue, h.NewValue, h.ChangedAtUtc)).ToList(),
        ticket.Tags.Select(t => t.Tag?.Name ?? t.TagId.ToString()).ToList());
}
