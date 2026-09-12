using Atlas.Application.Common;
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
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<UserService> _logger;

    public UserService(IUserRepository userRepository, IUnitOfWork unitOfWork, IAuditLogRepository auditLogRepository, ILogger<UserService> logger)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _auditLogRepository = auditLogRepository;
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

    public async Task<UserDto> ChangeRoleAsync(Guid id, ChangeUserRoleRequest request, Guid actorUserId, CancellationToken cancellationToken)
    {
        var user = await GetUserOrThrowAsync(id, cancellationToken);
        var oldRole = user.Role;

        user.ChangeRole(request.Role);
        await RecordAuditAsync(actorUserId, "RoleChanged", user.Id, new { role = oldRole }, new { role = user.Role }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Deliberately just a log line, not a JWT revocation: this project has
        // no token-revocation/refresh mechanism yet (Del 5 issues plain,
        // non-revocable JWTs), so a role change only takes effect the next
        // time the affected user logs in and gets a fresh set of "permission"
        // claims baked in — see the same note on RolePermissions itself. The
        // AuditLog row above is the durable record of the change itself
        // (who changed whose role to what, and when); this log line is just
        // for whoever is tailing the console right now.
        _logger.LogInformation("User {UserId} role changed to {Role} by {ActorUserId}", id, request.Role, actorUserId);
        return ToDto(user);
    }

    public async Task<UserDto> DeactivateAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken)
    {
        var user = await GetUserOrThrowAsync(id, cancellationToken);

        user.Deactivate();
        await RecordAuditAsync(actorUserId, "Deactivated", user.Id, new { isActive = true }, new { isActive = false }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {UserId} deactivated by {ActorUserId}", id, actorUserId);
        return ToDto(user);
    }

    public async Task<UserDto> ReactivateAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken)
    {
        var user = await GetUserOrThrowAsync(id, cancellationToken);

        user.Reactivate();
        await RecordAuditAsync(actorUserId, "Reactivated", user.Id, new { isActive = false }, new { isActive = true }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {UserId} reactivated by {ActorUserId}", id, actorUserId);
        return ToDto(user);
    }

    /// <summary>
    /// Stages one AuditLog row on the same tracked DbContext as the change
    /// just made above — not yet saved, so it commits atomically with that
    /// change on the SaveChangesAsync call each caller makes right after this
    /// returns. See IAuditLogRepository's doc comment for why that ordering
    /// (stage here, save once, in the caller) matters.
    /// </summary>
    private Task RecordAuditAsync(Guid actorUserId, string action, Guid userId, object? oldValues, object? newValues, CancellationToken cancellationToken) =>
        _auditLogRepository.AddAsync(
            AuditLog.Create(actorUserId, action, nameof(User), userId, AuditLogSerializer.ToJson(oldValues), AuditLogSerializer.ToJson(newValues)),
            cancellationToken);

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
