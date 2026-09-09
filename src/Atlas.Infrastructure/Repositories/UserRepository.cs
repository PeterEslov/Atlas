using Atlas.Application.Common.Interfaces;
using Atlas.Application.Users.Dtos;
using Atlas.Domain.Entities;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly AtlasDbContext _dbContext;

    public UserRepository(AtlasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.Users
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        return _dbContext.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);
    }

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        return _dbContext.Users.AnyAsync(u => u.Email == normalizedEmail, cancellationToken);
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken)
    {
        await _dbContext.Users.AddAsync(user, cancellationToken);
    }

    public async Task<(IReadOnlyList<User> Items, int TotalCount)> SearchAsync(UserListQuery query, CancellationToken cancellationToken)
    {
        IQueryable<User> filtered = _dbContext.Users.AsNoTracking();

        if (query.OrganizationId is not null)
        {
            filtered = filtered.Where(u => u.OrganizationId == query.OrganizationId);
        }

        if (query.Role is not null)
        {
            filtered = filtered.Where(u => u.Role == query.Role);
        }

        if (query.IsActive is not null)
        {
            filtered = filtered.Where(u => u.IsActive == query.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim()}%";
            filtered = filtered.Where(u => EF.Functions.Like(u.FullName, pattern) || EF.Functions.Like(u.Email, pattern));
        }

        var totalCount = await filtered.CountAsync(cancellationToken);

        var items = await filtered
            .Include(u => u.Organization)
            .OrderBy(u => u.FullName)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public void Update(User user)
    {
        // User was loaded from this same DbContext, so EF Core's change tracker
        // already knows about every mutation made through its domain methods
        // (ChangeRole/Deactivate/Reactivate). See TicketRepository.Update.
    }
}
