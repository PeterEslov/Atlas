using Atlas.Application.Projects.Dtos;
using Atlas.Domain.Entities;

namespace Atlas.Application.Common.Interfaces;

/// <summary>
/// Persistence contract for <see cref="Project"/>. Kept narrow and non-generic,
/// the same way ITicketRepository and IOrganizationRepository are — see
/// ITicketRepository's doc comment for why.
/// </summary>
public interface IProjectRepository
{
    /// <summary>Loads the project with its Members collection (UserId/JoinedAtUtc only — see ProjectMemberConfiguration).</summary>
    Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Joins ProjectMembers to Users for one project, so the "get one" endpoint
    /// can show each member's name without giving ProjectMember its own User
    /// navigation property (which would exist for this one read path only).
    /// </summary>
    Task<IReadOnlyList<(Guid UserId, string FullName, DateTime JoinedAtUtc)>> GetMemberDetailsAsync(Guid projectId, CancellationToken cancellationToken);

    Task<(IReadOnlyList<Project> Items, int TotalCount)> SearchAsync(ProjectListQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// One grouped COUNT query for every id in <paramref name="projectIds"/>,
    /// so listing a page of projects costs one extra query total, not one per
    /// project. See the doc comment on ProjectDto for why this exists.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> GetMemberCountsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken);

    Task AddAsync(Project project, CancellationToken cancellationToken);

    /// <summary>
    /// No-op for a change-tracked, attached entity — present so the intent is explicit
    /// at call sites. See the identical comment on ITicketRepository.Update.
    /// </summary>
    void Update(Project project);
}
