using Atlas.Application.Organizations.Dtos;
using Atlas.Domain.Entities;

namespace Atlas.Application.Common.Interfaces;

/// <summary>
/// Persistence contract for <see cref="Organization"/>. Implemented by Atlas.Infrastructure
/// against EF Core / Azure SQL. Kept narrow and non-generic, the same way
/// ITicketRepository is — see that interface's doc comment for why.
/// </summary>
public interface IOrganizationRepository
{
    Task<Organization?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Counts child rows (users, teams, projects) with three lightweight
    /// COUNT(*) queries rather than loading the related entities — see the
    /// doc comment on OrganizationDetailDto for why that distinction matters.
    /// </summary>
    Task<(int UserCount, int TeamCount, int ProjectCount)> GetChildCountsAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<(IReadOnlyList<Organization> Items, int TotalCount)> SearchAsync(OrganizationListQuery query, CancellationToken cancellationToken);

    /// <summary>Used to enforce "organization names are unique" before Organization.Create ever runs — see OrganizationService.CreateAsync.</summary>
    Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken);

    Task AddAsync(Organization organization, CancellationToken cancellationToken);

    /// <summary>
    /// No-op for a change-tracked, attached entity — present so the intent is explicit
    /// at call sites and so an alternate implementation has something to override.
    /// See the identical comment on ITicketRepository.Update for the full explanation.
    /// </summary>
    void Update(Organization organization);
}
