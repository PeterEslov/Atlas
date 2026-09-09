using Atlas.Application.Common.Exceptions;
using Atlas.Application.Common.Interfaces;
using Atlas.Application.Common.Models;
using Atlas.Application.Users.Dtos;
using Atlas.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Users.Services;

/// <summary>
/// Orchestrates user *management* use cases — listing, viewing, changing role,
/// deactivating/reactivating. Creating a user is deliberately NOT here: that's
/// still POST /api/auth/register (AuthService), because registration already
/// owns password hashing and there is no second, parallel "admin creates a
/// user with no password" flow yet. Duplicating that logic here would just be
/// two places that can drift out of sync — see RegisterRequest's own doc
/// comment for the note that this will need revisiting once self-registration
/// is closed off.
/// </summary>
public sealed class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UserService> _logger;

    public UserService(IUserRepository userRepository, IUnitOfWork unitOfWork, ILogger<UserService> logger)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<PagedResult<UserDto>> SearchAsync(UserListQuery query, CancellationToken cancellationToken)
    {
        var normalizedQuery = Normalize(query);
        var (items, totalCount) = await _userRepository.SearchAsync(normalizedQuery, cancellationToken);

        var dtos = items.Select(ToDto).ToList();
        return new PagedResult<UserDto>(dtos, normalizedQuery.Page, normalizedQuery.PageSize, totalCount);
    }

    public async Task<UserDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(id, cancellationToken);
        return user is null ? null : ToDto(user);
    }

    public async Task<UserDto> ChangeRoleAsync(Guid id, ChangeUserRoleRequest request, CancellationToken cancellationToken)
    {
        var user = await GetUserOrThrowAsync(id, cancellationToken);

        user.ChangeRole(request.Role);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Deliberately just a log line, not a JWT revocation: this project has
        // no token-revocation/refresh mechanism yet (Del 5 issues plain,
        // non-revocable JWTs), so a role change only takes effect the next
        // time the affected user logs in and gets a fresh set of "permission"
        // claims baked in — see the same note on RolePermissions itself.
        _logger.LogInformation("User {UserId} role changed to {Role}", id, request.Role);
        return ToDto(user);
    }

    public async Task<UserDto> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await GetUserOrThrowAsync(id, cancellationToken);

        user.Deactivate();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {UserId} deactivated", id);
        return ToDto(user);
    }

    public async Task<UserDto> ReactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await GetUserOrThrowAsync(id, cancellationToken);

        user.Reactivate();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {UserId} reactivated", id);
        return ToDto(user);
    }

    private async Task<User> GetUserOrThrowAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            throw new NotFoundException(nameof(User), id);
        }

        return user;
    }

    private static UserListQuery Normalize(UserListQuery query)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize switch
        {
            < 1 => 25,
            > UserListQuery.MaxPageSize => UserListQuery.MaxPageSize,
            _ => query.PageSize
        };

        return new UserListQuery
        {
            OrganizationId = query.OrganizationId,
            Role = query.Role,
            IsActive = query.IsActive,
            Search = query.Search,
            Page = page,
            PageSize = pageSize
        };
    }

    private static UserDto ToDto(User user) => new(
        user.Id,
        user.FullName,
        user.Email,
        user.Role,
        user.OrganizationId,
        user.Organization?.Name ?? string.Empty,
        user.IsActive,
        user.CreatedAtUtc);
}
