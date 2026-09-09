using Atlas.Application.Common.Exceptions;
using Atlas.Application.Common.Interfaces;
using Atlas.Application.Common.Models;
using Atlas.Application.Projects.Dtos;
using Atlas.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Projects.Services;

/// <summary>
/// Orchestrates project use cases — the same shape as OrganizationService and
/// UserService: load via the repository, invoke the domain method that owns
/// the business rule (Project.Create/AddMember/Archive), persist via the unit
/// of work, map to a DTO. There is deliberately no RemoveMember or Unarchive
/// here yet — Project's domain type doesn't expose either method (unlike Team,
/// which does support RemoveMember), so adding them to the service without a
/// domain method to call would just be putting the rule in the wrong layer.
/// Treat this as a known, honest gap rather than an oversight: it's easy to
/// add both later by adding the method to Project first.
/// </summary>
public sealed class ProjectService : IProjectService
{
    private readonly IProjectRepository _projectRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ProjectService> _logger;

    public ProjectService(IProjectRepository projectRepository, IUnitOfWork unitOfWork, ILogger<ProjectService> logger)
    {
        _projectRepository = projectRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<PagedResult<ProjectDto>> SearchAsync(ProjectListQuery query, CancellationToken cancellationToken)
    {
        var normalizedQuery = Normalize(query);
        var (items, totalCount) = await _projectRepository.SearchAsync(normalizedQuery, cancellationToken);

        // One grouped query for the whole page's member counts, not one per row —
        // see the doc comment on IProjectRepository.GetMemberCountsAsync.
        var memberCounts = await _projectRepository.GetMemberCountsAsync(items.Select(p => p.Id).ToList(), cancellationToken);

        var dtos = items.Select(p => ToDto(p, memberCounts.GetValueOrDefault(p.Id))).ToList();
        return new PagedResult<ProjectDto>(dtos, normalizedQuery.Page, normalizedQuery.PageSize, totalCount);
    }

    public async Task<ProjectDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var project = await _projectRepository.GetByIdAsync(id, cancellationToken);
        if (project is null)
        {
            return null;
        }

        var memberDetails = await _projectRepository.GetMemberDetailsAsync(id, cancellationToken);
        return ToDetailDto(project, memberDetails);
    }

    public async Task<ProjectDetailDto> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken)
    {
        var project = Project.Create(request.OrganizationId, request.Name, request.Description);

        await _projectRepository.AddAsync(project, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Project {ProjectId} '{Name}' created for organization {OrganizationId}", project.Id, project.Name, project.OrganizationId);

        // Brand new, so there are no members yet without a query.
        return ToDetailDto(project, []);
    }

    public async Task<ProjectDetailDto> ArchiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var project = await GetProjectOrThrowAsync(id, cancellationToken);

        project.Archive();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Project {ProjectId} archived", id);
        return ToDetailDto(project, await _projectRepository.GetMemberDetailsAsync(id, cancellationToken));
    }

    public async Task<ProjectDetailDto> AddMemberAsync(Guid id, AddProjectMemberRequest request, CancellationToken cancellationToken)
    {
        var project = await GetProjectOrThrowAsync(id, cancellationToken);

        // Project.AddMember throws DomainException if request.UserId is already
        // a member — that rule lives on the entity, not here, so it can never
        // be bypassed by a second caller of Project.AddMember down the line.
        project.AddMember(request.UserId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {UserId} added to project {ProjectId}", request.UserId, id);
        return ToDetailDto(project, await _projectRepository.GetMemberDetailsAsync(id, cancellationToken));
    }

    private async Task<Project> GetProjectOrThrowAsync(Guid id, CancellationToken cancellationToken)
    {
        var project = await _projectRepository.GetByIdAsync(id, cancellationToken);
        if (project is null)
        {
            throw new NotFoundException(nameof(Project), id);
        }

        return project;
    }

    private static ProjectListQuery Normalize(ProjectListQuery query)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize switch
        {
            < 1 => 25,
            > ProjectListQuery.MaxPageSize => ProjectListQuery.MaxPageSize,
            _ => query.PageSize
        };

        return new ProjectListQuery
        {
            OrganizationId = query.OrganizationId,
            IsArchived = query.IsArchived,
            Search = query.Search,
            Page = page,
            PageSize = pageSize
        };
    }

    private static ProjectDto ToDto(Project project, int memberCount) => new(
        project.Id,
        project.OrganizationId,
        project.Name,
        project.Description,
        project.IsArchived,
        memberCount,
        project.CreatedAtUtc);

    private static ProjectDetailDto ToDetailDto(Project project, IReadOnlyList<(Guid UserId, string FullName, DateTime JoinedAtUtc)> members) => new(
        project.Id,
        project.OrganizationId,
        project.Name,
        project.Description,
        project.IsArchived,
        members.Select(m => new ProjectMemberDto(m.UserId, m.FullName, m.JoinedAtUtc)).ToList(),
        project.CreatedAtUtc,
        project.ModifiedAtUtc);
}
