using Atlas.Application.Teams.Dtos;
using Atlas.Domain.Entities;

namespace Atlas.Application.Common.Interfaces;

/// <summary>
/// Persistence contract for <see cref="Team"/>. Mirrors IProjectRepository's
/// shape exactly — Team and Project are structurally the same kind of
/// "organization-scoped collection of members" problem, so it would be odd
/// for their repositories to look any different from each other.
/// </summary>
public interface ITeamRepository
{
    /// <summary>Loads the team with its Members collection (UserId/JoinedAtUtc only).</summary>
    Task<Team?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Joins TeamMembers to Users for one team — see IProjectRepository.GetMemberDetailsAsync for why this exists as its own query.</summary>
    Task<IReadOnlyList<(Guid UserId, string FullName, DateTime JoinedAtUtc)>> GetMemberDetailsAsync(Guid teamId, CancellationToken cancellationToken);

    Task<(IReadOnlyList<Team> Items, int TotalCount)> SearchAsync(TeamListQuery query, CancellationToken cancellationToken);

    /// <summary>One grouped COUNT query for every id in <paramref name="teamIds"/> — see IProjectRepository.GetMemberCountsAsync.</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetMemberCountsAsync(IReadOnlyCollection<Guid> teamIds, CancellationToken cancellationToken);

    Task AddAsync(Team team, CancellationToken cancellationToken);

    /// <summary>
    /// No-op for a change-tracked, attached entity — present so the intent is explicit
    /// at call sites. See the identical comment on ITicketRepository.Update.
    /// </summary>
    void Update(Team team);
}
