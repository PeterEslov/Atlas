using Atlas.Application.Common.Models;
using Atlas.Application.Users.Dtos;

namespace Atlas.Application.Users.Services;

public interface IUserService
{
    Task<PagedResult<UserDto>> SearchAsync(UserListQuery query, CancellationToken cancellationToken);

    Task<UserDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<UserDto> ChangeRoleAsync(Guid id, ChangeUserRoleRequest request, Guid actorUserId, CancellationToken cancellationToken);

    Task<UserDto> DeactivateAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken);

    Task<UserDto> ReactivateAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken);
}
