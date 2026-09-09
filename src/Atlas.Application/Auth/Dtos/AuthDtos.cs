using Atlas.Domain.Enums;

namespace Atlas.Application.Auth.Dtos;

/// <summary>
/// Open self-registration, kept simple for this phase: anyone can create an
/// account with any role and organization. There is no User.Manage-gated
/// "admin creates a user" flow yet — this is the demo/dev convenience until
/// that lands. Don't ship this endpoint open to the public as-is.
/// </summary>
public sealed record RegisterRequest(string FullName, string Email, string Password, Guid OrganizationId, UserRole Role);

public sealed record LoginRequest(string Email, string Password);

public sealed record AuthResponseDto(
    string Token,
    DateTime ExpiresAtUtc,
    Guid UserId,
    string FullName,
    string Email,
    UserRole Role,
    IReadOnlyList<string> Permissions);
