using Atlas.Application.Common.Models;
using Atlas.Application.Organizations.Dtos;

namespace Atlas.Application.Organizations.Services;

public interface IOrganizationService
{
    Task<PagedResult<OrganizationDto>> SearchAsync(OrganizationListQuery query, CancellationToken cancellationToken);

    Task<OrganizationDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<OrganizationDetailDto> CreateAsync(CreateOrganizationRequest request, Guid actorUserId, CancellationToken cancellationToken);

    Task<OrganizationDetailDto> RenameAsync(Guid id, RenameOrganizationRequest request, Guid actorUserId, CancellationToken cancellationToken);

    Task<OrganizationDetailDto> DeactivateAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken);

    Task<OrganizationDetailDto> ReactivateAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken);
}
