using Atlas.Application.Common.Interfaces;
using Atlas.Application.Teams.Dtos;
using Atlas.Domain.Entities;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="ITeamRepository"/> — mirrors ProjectRepository's shape exactly.</summary>
public sealed class TeamRepository : ITeamRepository
{
    private readonly AtlasDbContext _dbContext;

    public TeamRepository(AtlasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Team?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.Teams
            .Include(t => t.Members)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<(Guid UserId, string FullName, DateTime JoinedAtUtc)>> GetMemberDetailsAsync(Guid teamId, CancellationToken cancellationToken)
    {
        var members = await (
            from tm in _dbContext.TeamMembers.AsNoTracking()
            join u in _dbContext.Users.AsNoTracking() on tm.UserId equals u.Id
            where tm.TeamId == teamId
            orderby u.FullName
            select new { u.Id, u.FullName, tm.JoinedAtUtc }
        ).ToListAsync(cancellationToken);

        return members.Select(m => (m.Id, m.FullName, m.JoinedAtUtc)).ToList();
    }

    public async Task<(IReadOnlyList<Team> Items, int TotalCount)> SearchAsync(TeamListQuery query, CancellationToken cancellationToken)
    {
        IQueryable<Team> filtered = _dbContext.Teams.AsNoTracking();

        if (query.OrganizationId is not null)
        {
            filtered = filtered.Where(t => t.OrganizationId == query.OrganizationId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim()}%";
            filtered = filtered.Where(t => EF.Functions.Like(t.Name, pattern));
        }

        var totalCount = await filtered.CountAsync(cancellationToken);

        var items = await filtered
            .OrderBy(t => t.Name)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetMemberCountsAsync(IReadOnlyCollection<Guid> teamIds, CancellationToken cancellationToken)
    {
        if (teamIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        // See the identical comment in ProjectRepository.GetMemberCountsAsync —
        // one GROUP BY query for the page, dictionary built in memory afterward.
        var counts = await _dbContext.TeamMembers
            .AsNoTracking()
            .Where(tm => teamIds.Contains(tm.TeamId))
            .GroupBy(tm => tm.TeamId)
            .Select(g => new { TeamId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(x => x.TeamId, x => x.Count);
    }

    public async Task AddAsync(Team team, CancellationToken cancellationToken)
    {
        await _dbContext.Teams.AddAsync(team, cancellationToken);
    }

    public void Update(Team team)
    {
        // Team was loaded from this same DbContext, so EF Core's change tracker
        // already knows about every mutation made through its domain methods
        // (AddMember/RemoveMember). See TicketRepository.Update.
    }
}
