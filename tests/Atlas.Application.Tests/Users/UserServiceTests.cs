using Atlas.Application.Common.Exceptions;
using Atlas.Application.Common.Interfaces;
using Atlas.Application.Users.Dtos;
using Atlas.Application.Users.Services;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Atlas.Application.Tests.Users;

/// <summary>
/// Note what's deliberately NOT tested here: UsersController's guard against a
/// user changing their own role or deactivating their own account lives at the
/// controller layer (see docs/ARCHITECTURE.md's Authentication section), not
/// in UserService itself — that's covered by Atlas.Api.IntegrationTests
/// instead, since it's genuinely an HTTP-layer concern (who is ICurrentUserService
/// resolving the caller to be), not something a service-level unit test with a
/// mocked repository could exercise meaningfully.
/// </summary>
public class UserServiceTests
{
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAuditLogRepository> _auditLogRepository = new();

    private readonly Guid _organizationId = Guid.NewGuid();
    private readonly Guid _actorUserId = Guid.NewGuid();

    private UserService CreateSut() => new(
        _userRepository.Object,
        _unitOfWork.Object,
        _auditLogRepository.Object,
        NullLogger<UserService>.Instance);

    private User CreateAgent()
    {
        var user = User.Create(_organizationId, "Alice Agent", "alice@northstar-it.example", UserRole.Agent);
        _userRepository.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        return user;
    }

    [Fact]
    public async Task ChangeRoleAsync_UpdatesTheRoleAndWritesAnAuditRow()
    {
        var user = CreateAgent();
        var sut = CreateSut();

        var result = await sut.ChangeRoleAsync(user.Id, new ChangeUserRoleRequest(UserRole.Manager), _actorUserId, CancellationToken.None);

        Assert.Equal(UserRole.Manager, result.Role);
        _auditLogRepository.Verify(r => r.AddAsync(
            It.Is<AuditLog>(a => a.Action == "RoleChanged" && a.EntityName == nameof(User) && a.EntityId == user.Id && a.UserId == _actorUserId),
            It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangeRoleAsync_UnknownUser_ThrowsNotFoundException()
    {
        _userRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);
        var sut = CreateSut();

        await Assert.ThrowsAsync<NotFoundException>(
            () => sut.ChangeRoleAsync(Guid.NewGuid(), new ChangeUserRoleRequest(UserRole.Manager), _actorUserId, CancellationToken.None));
    }

    [Fact]
    public async Task DeactivateAsync_SetsIsActiveFalse_AndWritesAnAuditRow()
    {
        var user = CreateAgent();
        var sut = CreateSut();

        var result = await sut.DeactivateAsync(user.Id, _actorUserId, CancellationToken.None);

        Assert.False(result.IsActive);
        _auditLogRepository.Verify(r => r.AddAsync(
            It.Is<AuditLog>(a => a.Action == "Deactivated" && a.EntityId == user.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReactivateAsync_SetsIsActiveTrue_AndWritesAnAuditRow()
    {
        var user = CreateAgent();
        user.Deactivate();
        var sut = CreateSut();

        var result = await sut.ReactivateAsync(user.Id, _actorUserId, CancellationToken.None);

        Assert.True(result.IsActive);
        _auditLogRepository.Verify(r => r.AddAsync(
            It.Is<AuditLog>(a => a.Action == "Reactivated" && a.EntityId == user.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
