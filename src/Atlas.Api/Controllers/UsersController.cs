using Atlas.Application.Common.Exceptions;
using Atlas.Application.Common.Interfaces;
using Atlas.Application.Common.Models;
using Atlas.Application.Users.Dtos;
using Atlas.Application.Users.Services;
using Atlas.Domain.Exceptions;
using Atlas.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Api.Controllers;

/// <summary>
/// User management endpoints (Del 6) — listing, viewing, changing role,
/// deactivating/reactivating. Creating a user is still POST /api/auth/register
/// (self-registration); see the doc comment on UserService for why that isn't
/// duplicated here. Read access (User.Read) is granted to Manager and Admin;
/// write access (User.Manage) likewise — see RolePermissions for the reasoning.
/// </summary>
[ApiController]
[Route("api/users")]
[Produces("application/json")]
[Authorize]
public sealed class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ICurrentUserService _currentUserService;

    public UsersController(IUserService userService, ICurrentUserService currentUserService)
    {
        _userService = userService;
        _currentUserService = currentUserService;
    }

    /// <summary>See TicketsController.ActorUserId — same defensive fallback, same reason.</summary>
    private Guid ActorUserId =>
        _currentUserService.UserId
            ?? throw new AuthenticationException("The request's access token did not contain a valid user id.");

    /// <summary>
    /// GET /api/users?organizationId={guid}&amp;role=Agent&amp;isActive=true&amp;search=anna&amp;page=1&amp;pageSize=25
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Permissions.UserRead)]
    [ProducesResponseType(typeof(PagedResult<UserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<UserDto>>> Search([FromQuery] UserListQuery query, CancellationToken cancellationToken)
    {
        var result = await _userService.SearchAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.UserRead)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var user = await _userService.GetByIdAsync(id, cancellationToken);
        return user is null ? NotFound() : Ok(user);
    }

    /// <summary>
    /// POST /api/users/{id}/role
    ///
    /// Refuses to let a caller change their own role: without this, an Admin
    /// (or anyone who briefly ends up with User.Manage) could demote or
    /// promote themselves with nobody else in the loop. The check is on the
    /// caller's own id from the token, not on anything client-supplied, so
    /// it can't be bypassed by lying in the request body.
    /// </summary>
    [HttpPost("{id:guid}/role")]
    [Authorize(Policy = Permissions.UserManage)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserDto>> ChangeRole(
        Guid id,
        [FromBody] ChangeUserRoleRequest request,
        CancellationToken cancellationToken)
    {
        if (id == _currentUserService.UserId)
        {
            // A business-rule violation, not a credentials problem — DomainException
            // maps to 400 via ExceptionHandlingMiddleware, which is the right status
            // here (AuthenticationException would map to 401, which would be
            // misleading: the caller IS authenticated, the request is just invalid).
            throw new DomainException("You cannot change your own role.");
        }

        var updated = await _userService.ChangeRoleAsync(id, request, ActorUserId, cancellationToken);
        return Ok(updated);
    }

    /// <summary>POST /api/users/{id}/deactivate — same self-action guard as ChangeRole, so nobody can lock themselves out.</summary>
    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = Permissions.UserManage)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserDto>> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        if (id == _currentUserService.UserId)
        {
            throw new DomainException("You cannot deactivate your own account.");
        }

        var updated = await _userService.DeactivateAsync(id, ActorUserId, cancellationToken);
        return Ok(updated);
    }

    [HttpPost("{id:guid}/reactivate")]
    [Authorize(Policy = Permissions.UserManage)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        var updated = await _userService.ReactivateAsync(id, ActorUserId, cancellationToken);
        return Ok(updated);
    }
}
