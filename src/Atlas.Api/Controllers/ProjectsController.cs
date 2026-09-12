using Atlas.Application.Common.Exceptions;
using Atlas.Application.Common.Interfaces;
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
/// access is Manager+Admin; write access (create, archive/unarchive, add/
/// remove a member) is the same, on the same reasoning as User.Manage/
/// Team.Manage: this is day-to-day work inside an organization a Manager
/// already belongs to, not a tenant-boundary change like Organization.Manage.
/// Unarchive and RemoveMember were the last pieces of Del 7 — see Project.cs
/// for why they trailed Team's equivalent methods initially.
/// </summary>
[ApiController]
[Route("api/projects")]
[Produces("application/json")]
[Authorize]
public sealed class ProjectsController : ControllerBase
{
    private readonly IProjectService _projectService;
    private readonly ICurrentUserService _currentUserService;

    public ProjectsController(IProjectService projectService, ICurrentUserService currentUserService)
    {
        _projectService = projectService;
        _currentUserService = currentUserService;
    }

    /// <summary>See TicketsController.ActorUserId — same defensive fallback, same reason. Newly needed here for Del 15's audit trail (create/archive/unarchive now record who did it; AddMember/RemoveMember deliberately don't — see ProjectService.RecordAuditAsync).</summary>
    private Guid ActorUserId =>
        _currentUserService.UserId
            ?? throw new AuthenticationException("The request's access token did not contain a valid user id.");

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
        var created = await _projectService.CreateAsync(request, ActorUserId, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPost("{id:guid}/archive")]
    [Authorize(Policy = Permissions.ProjectManage)]
    [ProducesResponseType(typeof(ProjectDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectDetailDto>> Archive(Guid id, CancellationToken cancellationToken)
    {
        var updated = await _projectService.ArchiveAsync(id, ActorUserId, cancellationToken);
        return Ok(updated);
    }

    /// <summary>POST /api/projects/{id}/unarchive — closes the gap Del 7 originally left open; mirrors Archive.</summary>
    [HttpPost("{id:guid}/unarchive")]
    [Authorize(Policy = Permissions.ProjectManage)]
    [ProducesResponseType(typeof(ProjectDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectDetailDto>> Unarchive(Guid id, CancellationToken cancellationToken)
    {
        var updated = await _projectService.UnarchiveAsync(id, ActorUserId, cancellationToken);
        return Ok(updated);
    }

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

    /// <summary>
    /// DELETE /api/projects/{id}/members/{userId} — closes the other half of
    /// the Del 7 gap. Mirrors TeamsController.RemoveMember exactly; Project's
    /// domain type now has a RemoveMember method too (see Project.cs).
    /// </summary>
    [HttpDelete("{id:guid}/members/{userId:guid}")]
    [Authorize(Policy = Permissions.ProjectManage)]
    [ProducesResponseType(typeof(ProjectDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectDetailDto>> RemoveMember(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var updated = await _projectService.RemoveMemberAsync(id, userId, cancellationToken);
        return Ok(updated);
    }
}
