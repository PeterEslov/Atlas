using Atlas.Application.Common.Models;
using Atlas.Application.Projects.Dtos;

namespace Atlas.Application.Projects.Services;

public interface IProjectService
{
    Task<PagedResult<ProjectDto>> SearchAsync(ProjectListQuery query, CancellationToken cancellationToken);

    Task<ProjectDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<ProjectDetailDto> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken);

    Task<ProjectDetailDto> ArchiveAsync(Guid id, CancellationToken cancellationToken);

    Task<ProjectDetailDto> UnarchiveAsync(Guid id, CancellationToken cancellationToken);

    Task<ProjectDetailDto> AddMemberAsync(Guid id, AddProjectMemberRequest request, CancellationToken cancellationToken);

    Task<ProjectDetailDto> RemoveMemberAsync(Guid id, Guid userId, CancellationToken cancellationToken);
}
