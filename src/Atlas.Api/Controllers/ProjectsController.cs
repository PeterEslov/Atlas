using Atlas.Application.Common.Models;
using Atlas.Application.Projects.Dtos;
using Atlas.Application.Projects.Services;
using Atlas.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Api.Controllers;

/// <summary>
/// Project endpoints (Del 7). Project.Read/Project.Manage already existed in
/// Permissions before this Del — they were defined and granted to Manager and
/// Admin from early on, just never wired up to a controller until now. Read
/// access is Manager+Admin; write access (create, archive, add a member) is
/// the same, on the same reasoning as User.Manage/Team.Manage: this is
/// day-to-day work inside an organization a Manager already belongs to, not a
/// tenant-boundary change like Organization.Manage.
/// </summary>
[ApiController]
[Route("api/projects")]
[Produces("application/json")]
[Authorize]
public sealed class ProjectsController : ControllerBase
{
    private readonly IProjectService _projectService;

    public ProjectsController(IProjectService projectService)
    {
        _projectService = projectService;
    }

    /// <summary>
    /// GET /api/projects?organizationId={guid}&amp;isArchived=false&amp;search=onboarding&amp;page=1&amp;pageSize=25
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Permissions.ProjectRead)]
    [ProducesResponseType(typeof(PagedResult<ProjectDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ProjectDto>>> Search([FromQuery] ProjectListQuery query, CancellationToken cancellationToken)
    {
        var result = await _projectService.SearchAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.ProjectRead)]
    [ProducesResponseType(typeof(ProjectDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var project = await _projectService.GetByIdAsync(id, cancellationToken);
        return project is null ? NotFound() : Ok(project);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.ProjectManage)]
    [ProducesResponseType(typeof(ProjectDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProjectDetailDto>> Create(
        [FromBody] CreateProjectRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _projectService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPost("{id:guid}/archive")]
    [Authorize(Policy = Permissions.ProjectManage)]
    [ProducesResponseType(typeof(ProjectDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectDetailDto>> Archive(Guid id, CancellationToken cancellationToken)
    {
        var updated = await _projectService.ArchiveAsync(id, cancellationToken);
        return Ok(updated);
    }

    /// <summary>
    /// POST /api/projects/{id}/members
    ///
    /// There is deliberately no DELETE for a project member yet — Project's
    /// domain type has no RemoveMember method (Team's does; see TeamsController's
    /// matching endpoint) — see the doc comment on ProjectService for why that's
    /// an honest, known gap rather than something this endpoint quietly papers over.
    /// </summary>
    [HttpPost("{id:guid}/members")]
    [Authorize(Policy = Permissions.ProjectManage)]
    [ProducesResponseType(typeof(ProjectDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProjectDetailDto>> AddMember(
        Guid id,
        [FromBody] AddProjectMemberRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await _projectService.AddMemberAsync(id, request, cancellationToken);
        return Ok(updated);
    }
}
