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
}
