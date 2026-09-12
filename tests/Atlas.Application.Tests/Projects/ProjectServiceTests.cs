using Atlas.Application.Common.Interfaces;
using Atlas.Application.Projects.Dtos;
using Atlas.Application.Projects.Services;
using Atlas.Domain.Entities;
using Atlas.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Atlas.Application.Tests.Projects;

public class ProjectServiceTests
{
    private readonly Mock<IProjectRepository> _projectRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAuditLogRepository> _auditLogRepository = new();

    private readonly Guid _organizationId = Guid.NewGuid();
    private readonly Guid _actorUserId = Guid.NewGuid();

    private ProjectService CreateSut() => new(
        _projectRepository.Object,
        _unitOfWork.Object,
        _auditLogRepository.Object,
        NullLogger<ProjectService>.Instance);

    private Project CreateProject()
    {
        var project = Project.Create(_organizationId, "ACME Onboarding Q3", "Kickoff through go-live");
        _projectRepository.Setup(r => r.GetByIdAsync(project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(project);
        _projectRepository.Setup(r => r.GetMemberDetailsAsync(project.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, string, DateTime)>());
        return project;
    }

    [Fact]
    public async Task CreateAsync_WritesACreatedAuditRow()
    {
        var sut = CreateSut();

        await sut.CreateAsync(new CreateProjectRequest(_organizationId, "ACME Onboarding Q3", "Kickoff through go-live"), _actorUserId, CancellationToken.None);

        _auditLogRepository.Verify(r => r.AddAsync(
            It.Is<AuditLog>(a => a.Action == "Created" && a.EntityName == nameof(Project)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ArchiveAsync_ThenUnarchiveAsync_RoundTripsIsArchived_WithOneAuditRowEach()
    {
        var project = CreateProject();
        var sut = CreateSut();

        var archived = await sut.ArchiveAsync(project.Id, _actorUserId, CancellationToken.None);
        Assert.True(archived.IsArchived);

        var unarchived = await sut.UnarchiveAsync(project.Id, _actorUserId, CancellationToken.None);
        Assert.False(unarchived.IsArchived);

        _auditLogRepository.Verify(r => r.AddAsync(It.Is<AuditLog>(a => a.Action == "Archived"), It.IsAny<CancellationToken>()), Times.Once);
        _auditLogRepository.Verify(r => r.AddAsync(It.Is<AuditLog>(a => a.Action == "Unarchived"), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Del 15's own documented line: project membership churn is routine, not
    /// a lifecycle event worth a compliance trail entry, unlike Create/Archive/
    /// Unarchive above — see the doc comment on ProjectService.RecordAuditAsync.
    /// This is a regression test for that specific scope decision, not just
    /// "does adding a member work".
    /// </summary>
    [Fact]
    public async Task AddMemberAsync_AndRemoveMemberAsync_NeverWriteAnAuditRow()
    {
        var project = CreateProject();
        var userId = Guid.NewGuid();
        var sut = CreateSut();

        await sut.AddMemberAsync(project.Id, new AddProjectMemberRequest(userId), CancellationToken.None);
        await sut.RemoveMemberAsync(project.Id, userId, CancellationToken.None);

        _auditLogRepository.Verify(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddMemberAsync_SameUserTwice_ThrowsDomainExceptionOnTheSecondCall()
    {
        var project = CreateProject();
        var userId = Guid.NewGuid();
        var sut = CreateSut();

        await sut.AddMemberAsync(project.Id, new AddProjectMemberRequest(userId), CancellationToken.None);

        await Assert.ThrowsAsync<DomainException>(
            () => sut.AddMemberAsync(project.Id, new AddProjectMemberRequest(userId), CancellationToken.None));
    }

    [Fact]
    public async Task RemoveMemberAsync_UserWhoIsNotAMember_IsANoOp_NotAnException()
    {
        var project = CreateProject();
        var sut = CreateSut();

        // Should simply succeed — Project.RemoveMember is documented as a
        // deliberate no-op for a user who was never a member, not a 404/400.
        var result = await sut.RemoveMemberAsync(project.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(result.Members);
    }
}
