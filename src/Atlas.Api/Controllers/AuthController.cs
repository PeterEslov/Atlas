using Atlas.Application.Auth.Dtos;
using Atlas.Application.Auth.Services;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Api.Controllers;

/// <summary>
/// Registration and login for Del 5's local JWT authentication. Both endpoints
/// are anonymous by design — you can't authenticate your way into getting a
/// token. Every other controller in the API requires a valid bearer token from
/// here (or, from Del 20 onward, Azure Entra ID) plus the matching permission
/// policy.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// POST /api/auth/register
    ///
    /// Dev/demo convenience: the caller picks their own Role and OrganizationId.
    /// A real product would invite users into an organization or default every
    /// self-registration to Customer — see the doc comment on RegisterRequest.
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthResponseDto>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.RegisterAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// POST /api/auth/login
    /// </summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponseDto>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.LoginAsync(request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// GET /api/auth/organizations
    ///
    /// featurelogin branch: powers the Register form's organization dropdown
    /// (frontend/src/pages/LoginPage.tsx) so a new user picks an organization
    /// by name instead of pasting a raw GUID. Anonymous on purpose, same
    /// reasoning as Register/Login above — this has to be reachable before a
    /// token exists — but deliberately returns OrganizationOptionDto, not
    /// OrganizationsController's fuller OrganizationDto, so an anonymous
    /// visitor only ever sees {id, name} for active organizations.
    /// </summary>
    [HttpGet("organizations")]
    [ProducesResponseType(typeof(IReadOnlyList<OrganizationOptionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OrganizationOptionDto>>> GetRegistrableOrganizations(
        CancellationToken cancellationToken)
    {
        var result = await _authService.GetRegistrableOrganizationsAsync(cancellationToken);
        return Ok(result);
    }
}
