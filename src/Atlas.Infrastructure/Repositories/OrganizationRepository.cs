using Atlas.Application.Common.Interfaces;
using Atlas.Application.Organizations.Dtos;
using Atlas.Domain.Entities;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IOrganizationRepository"/> against Azure SQL / SQL Server.</summary>
public sealed class OrganizationRepository : IOrganizationRepository
{
    private readonly AtlasDbContext _dbContext;

    public OrganizationRepository(AtlasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Organization?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.Organizations.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public async Task<(int UserCount, int TeamCount, int ProjectCount)> GetChildCountsAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        // Three separate COUNT(*) statements rather than one Organization query
        // with .Include(o => o.Users)/.Include(o => o.Teams)/.Include(o => o.Projects)
        // — the latter would pull every row of every child table over the wire
        // just to report how many there are.
        var userCount = await _dbContext.Users.CountAsync(u => u.OrganizationId == organizationId, cancellationToken);
        var teamCount = await _dbContext.Teams.CountAsync(t => t.OrganizationId == organizationId, cancellationToken);
        var projectCount = await _dbContext.Projects.CountAsync(p => p.OrganizationId == organizationId, cancellationToken);

        return (userCount, teamCount, projectCount);
    }

    public async Task<(IReadOnlyList<Organization> Items, int TotalCount)> SearchAsync(OrganizationListQuery query, CancellationToken cancellationToken)
    {
        IQueryable<Organization> filtered = _dbContext.Organizations.AsNoTracking();

        if (query.Type is not null)
        {
            filtered = filtered.Where(o => o.Type == query.Type);
        }

        if (query.IsActive is not null)
        {
            filtered = filtered.Where(o => o.IsActive == query.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim()}%";
            filtered = filtered.Where(o => EF.Functions.Like(o.Name, pattern));
        }

        var totalCount = await filtered.CountAsync(cancellationToken);

        var items = await filtered
            .OrderBy(o => o.Name)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken)
    {
        var normalizedName = name.Trim();
        return _dbContext.Organizations.AnyAsync(o => o.Name == normalizedName, cancellationToken);
    }

    public async Task AddAsync(Organization organization, CancellationToken cancellationToken)
    {
        await _dbContext.Organizations.AddAsync(organization, cancellationToken);
    }

    public void Update(Organization organization)
    {
        // Organization was loaded from this same DbContext, so EF Core's change
        // tracker already knows about every mutation made through its domain
        // methods (Rename/Deactivate/Reactivate). See TicketRepository.Update.
    }
}
