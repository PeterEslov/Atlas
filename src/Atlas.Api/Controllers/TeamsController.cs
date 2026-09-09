using Atlas.Application.Common.Models;
using Atlas.Application.Teams.Dtos;
using Atlas.Application.Teams.Services;
using Atlas.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Api.Controllers;

/// <summary>
/// Team endpoints (Del 7) — internal organization teams like "Support Tier 1"
/// or "Platform Engineering", distinct from Project (which is externally-facing
/// work like "ACME Onboarding Q3"). Team.Read/Team.Manage are new permissions
/// introduced in this Del; see the doc comment on RolePermissions for why they
/// sit at the same Manager+Admin trust level as Project.Manage rather than the
/// Admin-only Organization.Manage.
/// </summary>
[ApiController]
[Route("api/teams")]
[Produces("application/json")]
[Authorize]
public sealed class TeamsController : ControllerBase
{
    private readonly ITeamService _teamService;

    public TeamsController(ITeamService teamService)
    {
        _teamService = teamService;
    }

    /// <summary>
    /// GET /api/teams?organizationId={guid}&amp;search=support&amp;page=1&amp;pageSize=25
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Permissions.TeamRead)]
    [ProducesResponseType(typeof(PagedResult<TeamDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<TeamDto>>> Search([FromQuery] TeamListQuery query, CancellationToken cancellationToken)
    {
        var result = await _teamService.SearchAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.TeamRead)]
    [ProducesResponseType(typeof(TeamDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TeamDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var team = await _teamService.GetByIdAsync(id, cancellationToken);
        return team is null ? NotFound() : Ok(team);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.TeamManage)]
    [ProducesResponseType(typeof(TeamDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TeamDetailDto>> Create(
        [FromBody] CreateTeamRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _teamService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPost("{id:guid}/members")]
    [Authorize(Policy = Permissions.TeamManage)]
    [ProducesResponseType(typeof(TeamDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TeamDetailDto>> AddMember(
        Guid id,
        [FromBody] AddTeamMemberRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await _teamService.AddMemberAsync(id, request, cancellationToken);
        return Ok(updated);
    }

    /// <summary>
    /// DELETE /api/teams/{id}/members/{userId} — unlike Project (see
    /// ProjectsController.AddMember's doc comment), Team's domain type does
    /// have a RemoveMember method, so this is a real endpoint, not a gap.
    /// </summary>
    [HttpDelete("{id:guid}/members/{userId:guid}")]
    [Authorize(Policy = Permissions.TeamManage)]
    [ProducesResponseType(typeof(TeamDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TeamDetailDto>> RemoveMember(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var updated = await _teamService.RemoveMemberAsync(id, userId, cancellationToken);
        return Ok(updated);
    }
}
