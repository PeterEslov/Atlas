using Atlas.Application.Common.Exceptions;
using Atlas.Application.Common.Interfaces;
using Atlas.Application.Common.Models;
using Atlas.Application.Organizations.Dtos;
using Atlas.Domain.Entities;
using Atlas.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Organizations.Services;

/// <summary>
/// Orchestrates organization use cases, the same shape as TicketService: load
/// via the repository, invoke the domain method that owns the business rule
/// (Organization.Create/Rename/Deactivate/Reactivate), persist via the unit of
/// work, map to a DTO. This is also, concretely, the fix for the gap that
/// surfaced while testing Del 5 — until now the only way to create an
/// organization was sql/002_SeedData.sql; POST /api/organizations replaces that.
/// </summary>
public sealed class OrganizationService : IOrganizationService
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<OrganizationService> _logger;

    public OrganizationService(
        IOrganizationRepository organizationRepository,
        IUnitOfWork unitOfWork,
        ILogger<OrganizationService> logger)
    {
        _organizationRepository = organizationRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<PagedResult<OrganizationDto>> SearchAsync(OrganizationListQuery query, CancellationToken cancellationToken)
    {
        var normalizedQuery = Normalize(query);
        var (items, totalCount) = await _organizationRepository.SearchAsync(normalizedQuery, cancellationToken);

        var dtos = items.Select(ToDto).ToList();
        return new PagedResult<OrganizationDto>(dtos, normalizedQuery.Page, normalizedQuery.PageSize, totalCount);
    }

    public async Task<OrganizationDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var organization = await _organizationRepository.GetByIdAsync(id, cancellationToken);
        if (organization is null)
        {
            return null;
        }

        var counts = await _organizationRepository.GetChildCountsAsync(id, cancellationToken);
        return ToDetailDto(organization, counts);
    }

    public async Task<OrganizationDetailDto> CreateAsync(CreateOrganizationRequest request, CancellationToken cancellationToken)
    {
        if (await _organizationRepository.NameExistsAsync(request.Name, cancellationToken))
        {
            // DomainException -> 400 via ExceptionHandlingMiddleware, same as every
            // other business-rule violation (e.g. Ticket's own validation).
            throw new DomainException($"An organization named '{request.Name}' already exists.");
        }

        var organization = Organization.Create(request.Name, request.Type);

        await _organizationRepository.AddAsync(organization, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Organization {OrganizationId} '{Name}' created", organization.Id, organization.Name);

        // Brand new, so its child counts are trivially zero without a query.
        return ToDetailDto(organization, (0, 0, 0));
    }

    public async Task<OrganizationDetailDto> RenameAsync(Guid id, RenameOrganizationRequest request, CancellationToken cancellationToken)
    {
        var organization = await GetOrganizationOrThrowAsync(id, cancellationToken);

        organization.Rename(request.Name);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Organization {OrganizationId} renamed to '{Name}'", id, request.Name);
        return ToDetailDto(organization, await _organizationRepository.GetChildCountsAsync(id, cancellationToken));
    }

    public async Task<OrganizationDetailDto> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var organization = await GetOrganizationOrThrowAsync(id, cancellationToken);

        organization.Deactivate();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Organization {OrganizationId} deactivated", id);
        return ToDetailDto(organization, await _organizationRepository.GetChildCountsAsync(id, cancellationToken));
    }

    public async Task<OrganizationDetailDto> ReactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var organization = await GetOrganizationOrThrowAsync(id, cancellationToken);

        organization.Reactivate();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Organization {OrganizationId} reactivated", id);
        return ToDetailDto(organization, await _organizationRepository.GetChildCountsAsync(id, cancellationToken));
    }

    private async Task<Organization> GetOrganizationOrThrowAsync(Guid id, CancellationToken cancellationToken)
    {
        var organization = await _organizationRepository.GetByIdAsync(id, cancellationToken);
        if (organization is null)
        {
            throw new NotFoundException(nameof(Organization), id);
        }

        return organization;
    }

    private static OrganizationListQuery Normalize(OrganizationListQuery query)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize switch
        {
            < 1 => 25,
            > OrganizationListQuery.MaxPageSize => OrganizationListQuery.MaxPageSize,
            _ => query.PageSize
        };

        return new OrganizationListQuery
        {
            Type = query.Type,
            IsActive = query.IsActive,
            Search = query.Search,
            Page = page,
            PageSize = pageSize
        };
    }

    private static OrganizationDto ToDto(Organization organization) => new(
        organization.Id,
        organization.Name,
        organization.Type,
        organization.IsActive,
        organization.CreatedAtUtc);

    private static OrganizationDetailDto ToDetailDto(Organization organization, (int UserCount, int TeamCount, int ProjectCount) counts) => new(
        organization.Id,
        organization.Name,
        organization.Type,
        organization.IsActive,
        counts.UserCount,
        counts.TeamCount,
        counts.ProjectCount,
        organization.CreatedAtUtc,
        organization.ModifiedAtUtc);
}
