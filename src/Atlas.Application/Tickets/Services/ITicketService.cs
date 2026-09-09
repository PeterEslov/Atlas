using Atlas.Application.Common.Models;
using Atlas.Application.Tickets.Dtos;

namespace Atlas.Application.Tickets.Services;

public interface ITicketService
{
    Task<PagedResult<TicketDto>> SearchAsync(TicketListQuery query, CancellationToken cancellationToken);

    Task<TicketDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<TicketDetailDto> CreateAsync(CreateTicketRequest request, Guid createdByUserId, CancellationToken cancellationToken);

    Task<TicketDetailDto> AssignAsync(Guid ticketId, AssignTicketRequest request, Guid actorUserId, CancellationToken cancellationToken);

    Task<TicketDetailDto> ChangeStatusAsync(Guid ticketId, ChangeTicketStatusRequest request, Guid actorUserId, CancellationToken cancellationToken);

    Task<TicketDetailDto> ChangePriorityAsync(Guid ticketId, ChangeTicketPriorityRequest request, Guid actorUserId, CancellationToken cancellationToken);

    Task<TicketCommentDto> AddCommentAsync(Guid ticketId, AddTicketCommentRequest request, Guid authorUserId, CancellationToken cancellationToken);

    /// <summary>Reopens a Closed or Cancelled ticket — see Ticket.Reopen for the state-transition rule.</summary>
    Task<TicketDetailDto> ReopenAsync(Guid ticketId, Guid actorUserId, CancellationToken cancellationToken);

    /// <summary>Tags a ticket by name, get-or-creating the Tag row within the ticket's own organization.</summary>
    Task<TicketDetailDto> AddTagAsync(Guid ticketId, AddTicketTagRequest request, Guid actorUserId, CancellationToken cancellationToken);

    /// <summary>Removes a tag from a ticket by name. A no-op — not a 404 — if the ticket doesn't have that tag.</summary>
    Task<TicketDetailDto> RemoveTagAsync(Guid ticketId, RemoveTicketTagRequest request, Guid actorUserId, CancellationToken cancellationToken);

    /// <summary>Hard-deletes a ticket. See the doc comment on the implementation for why this is distinct from a status change.</summary>
    Task DeleteAsync(Guid ticketId, CancellationToken cancellationToken);
}
