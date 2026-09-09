using Atlas.Application.Common.Interfaces;

namespace Atlas.Api.Services;

/// <summary>
/// Reads the caller's identity from HttpContext.User. Until Del 5 wires up real
/// authentication (JWT / Entra ID), no claims are populated, so every property
/// returns null/false — callers must not assume a signed-in user exists yet.
/// </summary>
public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public Guid? UserId => TryGetGuidClaim("sub");

    public Guid? OrganizationId => TryGetGuidClaim("organization_id");

    private Guid? TryGetGuidClaim(string claimType)
    {
        var value = _httpContextAccessor.HttpContext?.User.FindFirst(claimType)?.Value;
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
