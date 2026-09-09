using Atlas.Domain.Enums;

namespace Atlas.Application.Users.Dtos;

/// <summary>
/// Shape returned by both the list endpoint and the "get one" endpoint —
/// unlike Ticket, User doesn't need a separate summary/detail split, since
/// there's no expensive child collection (comments, history) to leave out of
/// a list response. Deliberately excludes PasswordHash, which never leaves
/// Atlas.Infrastructure/Auth in any DTO.
/// </summary>
public sealed record UserDto(
    Guid Id,
    string FullName,
    string Email,
    UserRole Role,
    Guid OrganizationId,
    string OrganizationName,
    bool IsActive,
    DateTime CreatedAtUtc);

/// <summary>Request body for POST /api/users/{id}/role.</summary>
public sealed record ChangeUserRoleRequest(UserRole Role);

/// <summary>
/// Filter/sort/paging parameters for GET /api/users, mirroring
/// GET /api/users?organizationId={guid}&amp;role=Agent&amp;isActive=true&amp;search=anna&amp;page=1&amp;pageSize=25
/// </summary>
public sealed class UserListQuery
{
    public Guid? OrganizationId { get; init; }
    public UserRole? Role { get; init; }
    public bool? IsActive { get; init; }
    public string? Search { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;

    public const int MaxPageSize = 100;
}
