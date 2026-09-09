using Atlas.Domain.Enums;

namespace Atlas.Application.Organizations.Dtos;

/// <summary>Summary shape returned by the list/search endpoint — no child-entity counts, cheap to project.</summary>
public sealed record OrganizationDto(
    Guid Id,
    string Name,
    OrganizationType Type,
    bool IsActive,
    DateTime CreatedAtUtc);

/// <summary>
/// Full shape returned by the "get one" endpoint. The three counts are read via
/// separate COUNT(*) queries (see OrganizationRepository.GetChildCountsAsync)
/// rather than by eager-loading the full Users/Teams/Projects collections —
/// for an organization with thousands of users, "how many" should never mean
/// "materialize every row and call .Count on the list in memory".
/// </summary>
public sealed record OrganizationDetailDto(
    Guid Id,
    string Name,
    OrganizationType Type,
    bool IsActive,
    int UserCount,
    int TeamCount,
    int ProjectCount,
    DateTime CreatedAtUtc,
    DateTime? ModifiedAtUtc);

/// <summary>Request body for POST /api/organizations.</summary>
public sealed record CreateOrganizationRequest(string Name, OrganizationType Type);

/// <summary>Request body for POST /api/organizations/{id}/rename.</summary>
public sealed record RenameOrganizationRequest(string Name);

/// <summary>
/// Filter/sort/paging parameters for GET /api/organizations, mirroring
/// GET /api/organizations?type=Customer&amp;isActive=true&amp;search=acme&amp;page=1&amp;pageSize=25
/// </summary>
public sealed class OrganizationListQuery
{
    public OrganizationType? Type { get; init; }
    public bool? IsActive { get; init; }
    public string? Search { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;

    public const int MaxPageSize = 100;
}
