using Atlas.Application.Common.Models;
using Atlas.Application.Teams.Dtos;

namespace Atlas.Application.Teams.Services;

public interface ITeamService
{
    Task<PagedResult<TeamDto>> SearchAsync(TeamListQuery query, CancellationToken cancellationToken);

    Task<TeamDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<TeamDetailDto> CreateAsync(CreateTeamRequest request, CancellationToken cancellationToken);

    Task<TeamDetailDto> AddMemberAsync(Guid id, AddTeamMemberRequest request, CancellationToken cancellationToken);

    Task<TeamDetailDto> RemoveMemberAsync(Guid id, Guid userId, CancellationToken cancellationToken);
}
