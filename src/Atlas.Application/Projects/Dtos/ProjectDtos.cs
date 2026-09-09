namespace Atlas.Application.Projects.Dtos;

/// <summary>
/// Summary shape returned by the list/search endpoint. MemberCount comes from
/// a single grouped COUNT query across the whole page (see
/// ProjectRepository.GetMemberCountsAsync), never from loading every
/// ProjectMember row for every project just to call .Count on it in memory.
/// </summary>
public sealed record ProjectDto(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string Description,
    bool IsArchived,
    int MemberCount,
    DateTime CreatedAtUtc);

/// <summary>
/// Full shape returned by the "get one" endpoint, including each member's
/// display name. ProjectMember only stores a UserId (see the doc comment on
/// ProjectMemberConfiguration for why it has no User navigation), so the
/// names here come from a dedicated join query, not from the loaded entity.
/// </summary>
public sealed record ProjectDetailDto(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string Description,
    bool IsArchived,
    IReadOnlyList<ProjectMemberDto> Members,
    DateTime CreatedAtUtc,
    DateTime? ModifiedAtUtc);

public sealed record ProjectMemberDto(Guid UserId, string FullName, DateTime JoinedAtUtc);

/// <summary>Request body for POST /api/projects.</summary>
public sealed record CreateProjectRequest(Guid OrganizationId, string Name, string Description = "");

/// <summary>Request body for POST /api/projects/{id}/members.</summary>
public sealed record AddProjectMemberRequest(Guid UserId);

/// <summary>
/// Filter/sort/paging parameters for GET /api/projects, mirroring
/// GET /api/projects?organizationId={guid}&amp;isArchived=false&amp;search=onboarding&amp;page=1&amp;pageSize=25
/// </summary>
public sealed class ProjectListQuery
{
    public Guid? OrganizationId { get; init; }
    public bool? IsArchived { get; init; }
    public string? Search { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;

    public const int MaxPageSize = 100;
}
