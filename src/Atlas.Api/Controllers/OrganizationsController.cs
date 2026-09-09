using Atlas.Application.Common.Models;
using Atlas.Application.Organizations.Dtos;
using Atlas.Application.Organizations.Services;
using Atlas.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Api.Controllers;

/// <summary>
/// Organization endpoints (Del 6). Read access (Organization.Read) is granted
/// to Manager and Admin; write access (Organization.Manage — create, rename,
/// deactivate, reactivate) is Admin-only, since an organization is a tenant
/// boundary rather than something within a Manager's own team. See the doc
/// comment on RolePermissions for the full reasoning. This directly replaces
/// the earlier workaround of inserting organizations via sql/002_SeedData.sql.
/// </summary>
[ApiController]
[Route("api/organizations")]
[Produces("application/json")]
[Authorize]
public sealed class OrganizationsController : ControllerBase
{
    private readonly IOrganizationService _organizationService;

    public OrganizationsController(IOrganizationService organizationService)
    {
        _organizationService = organizationService;
    }

    /// <summary>
    /// GET /api/organizations?type=Customer&amp;isActive=true&amp;search=acme&amp;page=1&amp;pageSize=25
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Permissions.OrganizationRead)]
    [ProducesResponseType(typeof(PagedResult<OrganizationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<OrganizationDto>>> Search([FromQuery] OrganizationListQuery query, CancellationToken cancellationToken)
    {
        var result = await _organizationService.SearchAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.OrganizationRead)]
    [ProducesResponseType(typeof(OrganizationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrganizationDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var organization = await _organizationService.GetByIdAsync(id, cancellationToken);
        return organization is null ? NotFound() : Ok(organization);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.OrganizationManage)]
    [ProducesResponseType(typeof(OrganizationDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OrganizationDetailDto>> Create(
        [FromBody] CreateOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _organizationService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPost("{id:guid}/rename")]
    [Authorize(Policy = Permissions.OrganizationManage)]
    [ProducesResponseType(typeof(OrganizationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OrganizationDetailDto>> Rename(
        Guid id,
        [FromBody] RenameOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await _organizationService.RenameAsync(id, request, cancellationToken);
        return Ok(updated);
    }

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = Permissions.OrganizationManage)]
    [ProducesResponseType(typeof(OrganizationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrganizationDetailDto>> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        var updated = await _organizationService.DeactivateAsync(id, cancellationToken);
        return Ok(updated);
    }

    [HttpPost("{id:guid}/reactivate")]
    [Authorize(Policy = Permissions.OrganizationManage)]
    [ProducesResponseType(typeof(OrganizationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrganizationDetailDto>> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        var updated = await _organizationService.ReactivateAsync(id, cancellationToken);
        return Ok(updated);
    }
}
