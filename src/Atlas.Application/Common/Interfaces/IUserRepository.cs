using Atlas.Application.Users.Dtos;
using Atlas.Domain.Entities;

namespace Atlas.Application.Common.Interfaces;

public interface IUserRepository
{
    /// <summary>
    /// Eagerly loads the user's Organization navigation property — added in
    /// Del 6 for the "get one user" endpoint, which needs OrganizationName.
    /// Safe to change from a plain lookup: at the time this changed,
    /// GetByIdAsync had no callers yet (AuthService uses GetByEmailAsync).
    /// </summary>
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken);

    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);

    Task AddAsync(User user, CancellationToken cancellationToken);

    Task<(IReadOnlyList<User> Items, int TotalCount)> SearchAsync(UserListQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// No-op for a change-tracked, attached entity — present so the intent is explicit
    /// at call sites. See the identical comment on ITicketRepository.Update.
    /// </summary>
    void Update(User user);
}
