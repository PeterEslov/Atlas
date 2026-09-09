namespace Atlas.Application.Teams.Dtos;

/// <summary>
/// Summary shape returned by the list/search endpoint. MemberCount comes from
/// a single grouped COUNT query across the whole page (see
/// TeamRepository.GetMemberCountsAsync) — the same pattern ProjectRepository
/// uses, for the same reason.
/// </summary>
public sealed record TeamDto(
    Guid Id,
    Guid OrganizationId,
    string Name,
    int MemberCount,
    DateTime CreatedAtUtc);

/// <summary>
/// Full shape returned by the "get one" endpoint, including each member's
/// display name — TeamMember, like ProjectMember, stores only a UserId, so
/// the names come from a dedicated join query rather than the loaded entity.
/// </summary>
public sealed record TeamDetailDto(
    Guid Id,
    Guid OrganizationId,
    string Name,
    IReadOnlyList<TeamMemberDto> Members,
    DateTime CreatedAtUtc,
    DateTime? ModifiedAtUtc);

public sealed record TeamMemberDto(Guid UserId, string FullName, DateTime JoinedAtUtc);

/// <summary>Request body for POST /api/teams.</summary>
public sealed record CreateTeamRequest(Guid OrganizationId, string Name);

/// <summary>Request body for POST /api/teams/{id}/members.</summary>
public sealed record AddTeamMemberRequest(Guid UserId);

/// <summary>
/// Filter/sort/paging parameters for GET /api/teams, mirroring
/// GET /api/teams?organizationId={guid}&amp;search=support&amp;page=1&amp;pageSize=25
/// </summary>
public sealed class TeamListQuery
{
    public Guid? OrganizationId { get; init; }
    public string? Search { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;

    public const int MaxPageSize = 100;
}
