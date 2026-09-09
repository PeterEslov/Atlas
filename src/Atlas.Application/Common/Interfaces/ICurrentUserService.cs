namespace Atlas.Application.Common.Interfaces;

/// <summary>
/// Exposes the identity of the caller of the current request, derived from the
/// JWT/claims once Del 5 (authentication) lands. Implemented in Atlas.Api against
/// HttpContext so Atlas.Application never takes a direct dependency on ASP.NET Core.
/// </summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }
    Guid? OrganizationId { get; }
    bool IsAuthenticated { get; }
}
