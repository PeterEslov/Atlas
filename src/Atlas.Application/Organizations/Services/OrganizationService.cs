using Atlas.Application.Common;
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
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<OrganizationService> _logger;

    public OrganizationService(
        IOrganizationRepository organizationRepository,
        IUnitOfWork unitOfWork,
        IAuditLogRepository auditLogRepository,
        ILogger<OrganizationService> logger)
    {
        _organizationRepository = organizationRepository;
        _unitOfWork = unitOfWork;
        _auditLogRepository = auditLogRepository;
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

    public async Task<OrganizationDetailDto> CreateAsync(CreateOrganizationRequest request, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (await _organizationRepository.NameExistsAsync(request.Name, cancellationToken))
        {
            // DomainException -> 400 via ExceptionHandlingMiddleware, same as every
            // other business-rule violation (e.g. Ticket's own validation).
            throw new DomainException($"An organization named '{request.Name}' already exists.");
        }

        var organization = Organization.Create(request.Name, request.Type);

        await _organizationRepository.AddAsync(organization, cancellationToken);
        await RecordAuditAsync(actorUserId, "Created", organization.Id, null, new { name = organization.Name, type = organization.Type }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Organization {OrganizationId} '{Name}' created by {ActorUserId}", organization.Id, organization.Name, actorUserId);

        // Brand new, so its child counts are trivially zero without a query.
        return ToDetailDto(organization, (0, 0, 0));
    }

    public async Task<OrganizationDetailDto> RenameAsync(Guid id, RenameOrganizationRequest request, Guid actorUserId, CancellationToken cancellationToken)
    {
        var organization = await GetOrganizationOrThrowAsync(id, cancellationToken);
        var oldName = organization.Name;

        organization.Rename(request.Name);
        await RecordAuditAsync(actorUserId, "Renamed", organization.Id, new { name = oldName }, new { name = organization.Name }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Organization {OrganizationId} renamed to '{Name}' by {ActorUserId}", id, request.Name, actorUserId);
        return ToDetailDto(organization, await _organizationRepository.GetChildCountsAsync(id, cancellationToken));
    }

    public async Task<OrganizationDetailDto> DeactivateAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken)
    {
        var organization = await GetOrganizationOrThrowAsync(id, cancellationToken);

        organization.Deactivate();
        await RecordAuditAsync(actorUserId, "Deactivated", organization.Id, new { isActive = true }, new { isActive = false }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Organization {OrganizationId} deactivated by {ActorUserId}", id, actorUserId);
        return ToDetailDto(organization, await _organizationRepository.GetChildCountsAsync(id, cancellationToken));
    }

    public async Task<OrganizationDetailDto> ReactivateAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken)
    {
        var organization = await GetOrganizationOrThrowAsync(id, cancellationToken);

        organization.Reactivate();
        await RecordAuditAsync(actorUserId, "Reactivated", organization.Id, new { isActive = false }, new { isActive = true }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Organization {OrganizationId} reactivated by {ActorUserId}", id, actorUserId);
        return ToDetailDto(organization, await _organizationRepository.GetChildCountsAsync(id, cancellationToken));
    }

    /// <summary>See UserService.RecordAuditAsync — same "stage now, save once in the caller" reasoning.</summary>
    private Task RecordAuditAsync(Guid actorUserId, string action, Guid organizationId, object? oldValues, object? newValues, CancellationToken cancellationToken) =>
        _auditLogRepository.AddAsync(
            AuditLog.Create(actorUserId, action, nameof(Organization), organizationId, AuditLogSerializer.ToJson(oldValues), AuditLogSerializer.ToJson(newValues)),
            cancellationToken);

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
