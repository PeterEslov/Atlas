using Atlas.Application.Common.Interfaces;
using Atlas.Application.Projects.Dtos;
using Atlas.Domain.Entities;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IProjectRepository"/> against Azure SQL / SQL Server.</summary>
public sealed class ProjectRepository : IProjectRepository
{
    private readonly AtlasDbContext _dbContext;

    public ProjectRepository(AtlasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.Projects
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<(Guid UserId, string FullName, DateTime JoinedAtUtc)>> GetMemberDetailsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var members = await (
            from pm in _dbContext.ProjectMembers.AsNoTracking()
            join u in _dbContext.Users.AsNoTracking() on pm.UserId equals u.Id
            where pm.ProjectId == projectId
            orderby u.FullName
            select new { u.Id, u.FullName, pm.JoinedAtUtc }
        ).ToListAsync(cancellationToken);

        return members.Select(m => (m.Id, m.FullName, m.JoinedAtUtc)).ToList();
    }

    public async Task<(IReadOnlyList<Project> Items, int TotalCount)> SearchAsync(ProjectListQuery query, CancellationToken cancellationToken)
    {
        IQueryable<Project> filtered = _dbContext.Projects.AsNoTracking();

        if (query.OrganizationId is not null)
        {
            filtered = filtered.Where(p => p.OrganizationId == query.OrganizationId);
        }

        if (query.IsArchived is not null)
        {
            filtered = filtered.Where(p => p.IsArchived == query.IsArchived);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim()}%";
            filtered = filtered.Where(p => EF.Functions.Like(p.Name, pattern));
        }

        var totalCount = await filtered.CountAsync(cancellationToken);

        var items = await filtered
            .OrderBy(p => p.Name)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetMemberCountsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken)
    {
        if (projectIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        // One GROUP BY query for the whole page of ids, rather than a separate
        // COUNT(*) per project (which is what OrganizationRepository.GetChildCountsAsync
        // does — but that's for a single organization; doing that once per row
        // of a 25-row page would be a textbook N+1). The dictionary itself is
        // built in memory after materializing — EF Core translates the query,
        // plain LINQ-to-Objects builds the lookup from the (small) result set.
        var counts = await _dbContext.ProjectMembers
            .AsNoTracking()
            .Where(pm => projectIds.Contains(pm.ProjectId))
            .GroupBy(pm => pm.ProjectId)
            .Select(g => new { ProjectId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(x => x.ProjectId, x => x.Count);
    }

    public async Task AddAsync(Project project, CancellationToken cancellationToken)
    {
        await _dbContext.Projects.AddAsync(project, cancellationToken);
    }

    public void Update(Project project)
    {
        // Project was loaded from this same DbContext, so EF Core's change
        // tracker already knows about every mutation made through its domain
        // methods (AddMember/Archive). See TicketRepository.Update.
    }
}
