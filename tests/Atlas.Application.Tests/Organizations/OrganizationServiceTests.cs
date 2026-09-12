using Atlas.Application.Common.Exceptions;
using Atlas.Application.Common.Interfaces;
using Atlas.Application.Organizations.Dtos;
using Atlas.Application.Organizations.Services;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Atlas.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Atlas.Application.Tests.Organizations;

public class OrganizationServiceTests
{
    private readonly Mock<IOrganizationRepository> _organizationRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAuditLogRepository> _auditLogRepository = new();

    private readonly Guid _actorUserId = Guid.NewGuid();

    private OrganizationService CreateSut() => new(
        _organizationRepository.Object,
        _unitOfWork.Object,
        _auditLogRepository.Object,
        NullLogger<OrganizationService>.Instance);

    private Organization CreateOrganization(string name = "Contoso Ltd")
    {
        var organization = Organization.Create(name, OrganizationType.Customer);
        _organizationRepository.Setup(r => r.GetByIdAsync(organization.Id, It.IsAny<CancellationToken>())).ReturnsAsync(organization);
        _organizationRepository.Setup(r => r.GetChildCountsAsync(organization.Id, It.IsAny<CancellationToken>())).ReturnsAsync((0, 0, 0));
        return organization;
    }

    [Fact]
    public async Task CreateAsync_WithAUniqueName_CreatesTheOrganizationAndWritesACreatedAuditRowWithNoOldValues()
    {
        _organizationRepository.Setup(r => r.NameExistsAsync("Contoso Ltd", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var sut = CreateSut();

        var result = await sut.CreateAsync(new CreateOrganizationRequest("Contoso Ltd", OrganizationType.Customer), _actorUserId, CancellationToken.None);

        Assert.Equal("Contoso Ltd", result.Name);
        Assert.Equal(0, result.UserCount);
        _organizationRepository.Verify(r => r.AddAsync(It.IsAny<Organization>(), It.IsAny<CancellationToken>()), Times.Once);
        _auditLogRepository.Verify(r => r.AddAsync(
            It.Is<AuditLog>(a => a.Action == "Created" && a.EntityName == nameof(Organization) && a.OldValuesJson == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithANameThatAlreadyExists_ThrowsDomainExceptionWithoutTouchingTheRepository()
    {
        _organizationRepository.Setup(r => r.NameExistsAsync("Contoso Ltd", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var sut = CreateSut();

        await Assert.ThrowsAsync<DomainException>(
            () => sut.CreateAsync(new CreateOrganizationRequest("Contoso Ltd", OrganizationType.Customer), _actorUserId, CancellationToken.None));

        _organizationRepository.Verify(r => r.AddAsync(It.IsAny<Organization>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RenameAsync_UpdatesTheNameAndWritesAnAuditRowWithBothOldAndNewNames()
    {
        var organization = CreateOrganization("Contoso Ltd");
        var sut = CreateSut();

        var result = await sut.RenameAsync(organization.Id, new RenameOrganizationRequest("Contoso GmbH"), _actorUserId, CancellationToken.None);

        Assert.Equal("Contoso GmbH", result.Name);
        _auditLogRepository.Verify(r => r.AddAsync(
            It.Is<AuditLog>(a => a.Action == "Renamed"
                && (a.OldValuesJson ?? string.Empty).Contains("Contoso Ltd")
                && (a.NewValuesJson ?? string.Empty).Contains("Contoso GmbH")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeactivateAsync_ThenReactivateAsync_RoundTripsIsActive_WithOneAuditRowEach()
    {
        var organization = CreateOrganization();
        var sut = CreateSut();

        var deactivated = await sut.DeactivateAsync(organization.Id, _actorUserId, CancellationToken.None);
        Assert.False(deactivated.IsActive);

        var reactivated = await sut.ReactivateAsync(organization.Id, _actorUserId, CancellationToken.None);
        Assert.True(reactivated.IsActive);

        _auditLogRepository.Verify(r => r.AddAsync(It.Is<AuditLog>(a => a.Action == "Deactivated"), It.IsAny<CancellationToken>()), Times.Once);
        _auditLogRepository.Verify(r => r.AddAsync(It.Is<AuditLog>(a => a.Action == "Reactivated"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RenameAsync_UnknownOrganization_ThrowsNotFoundException()
    {
        _organizationRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Organization?)null);
        var sut = CreateSut();

        await Assert.ThrowsAsync<NotFoundException>(
            () => sut.RenameAsync(Guid.NewGuid(), new RenameOrganizationRequest("New name"), _actorUserId, CancellationToken.None));
    }
}
