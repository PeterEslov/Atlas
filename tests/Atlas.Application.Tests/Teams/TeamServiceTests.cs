using Atlas.Application.Common.Exceptions;
using Atlas.Application.Common.Interfaces;
using Atlas.Application.Teams.Dtos;
using Atlas.Application.Teams.Services;
using Atlas.Domain.Entities;
using Atlas.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Atlas.Application.Tests.Teams;

/// <summary>
/// TeamService's constructor takes no IAuditLogRepository at all — Team never
/// got an audit hook in Del 15 the way User/Organization/Project/Ticket did,
/// so there's nothing to assert about audit rows here (unlike
/// ProjectServiceTests, which explicitly tests that membership churn does
/// NOT write one). These tests cover the same "load, mutate via the domain
/// method, persist" orchestration shape TeamService shares with ProjectService.
/// </summary>
public class TeamServiceTests
{
    private readonly Mock<ITeamRepository> _teamRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private readonly Guid _organizationId = Guid.NewGuid();

    private TeamService CreateSut() => new(_teamRepository.Object, _unitOfWork.Object, NullLogger<TeamService>.Instance);

    private Team CreateTeam()
    {
        var team = Team.Create(_organizationId, "Support Tier 1");
        _teamRepository.Setup(r => r.GetByIdAsync(team.Id, It.IsAny<CancellationToken>())).ReturnsAsync(team);
        _teamRepository.Setup(r => r.GetMemberDetailsAsync(team.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, string, DateTime)>());
        return team;
    }

    [Fact]
    public async Task CreateAsync_PersistsTheTeamAndSavesOnce()
    {
        var sut = CreateSut();

        var result = await sut.CreateAsync(new CreateTeamRequest(_organizationId, "Support Tier 1"), CancellationToken.None);

        Assert.Equal("Support Tier 1", result.Name);
        _teamRepository.Verify(r => r.AddAsync(It.IsAny<Team>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddMemberAsync_TwiceForTheSameUser_ThrowsDomainExceptionOnTheSecondCall()
    {
        var team = CreateTeam();
        var userId = Guid.NewGuid();
        var sut = CreateSut();

        await sut.AddMemberAsync(team.Id, new AddTeamMemberRequest(userId), CancellationToken.None);

        await Assert.ThrowsAsync<DomainException>(
            () => sut.AddMemberAsync(team.Id, new AddTeamMemberRequest(userId), CancellationToken.None));
    }

    [Fact]
    public async Task RemoveMemberAsync_UserWhoIsNotAMember_IsANoOp()
    {
        var team = CreateTeam();
        var sut = CreateSut();

        var result = await sut.RemoveMemberAsync(team.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(result.Members);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownTeam_ReturnsNull()
    {
        _teamRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Team?)null);
        var sut = CreateSut();

        var result = await sut.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task AddMemberAsync_UnknownTeam_ThrowsNotFoundException()
    {
        _teamRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Team?)null);
        var sut = CreateSut();

        await Assert.ThrowsAsync<NotFoundException>(
            () => sut.AddMemberAsync(Guid.NewGuid(), new AddTeamMemberRequest(Guid.NewGuid()), CancellationToken.None));
    }
}
