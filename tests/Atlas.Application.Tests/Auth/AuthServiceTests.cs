using Atlas.Application.Auth.Dtos;
using Atlas.Application.Auth.Services;
using Atlas.Application.Common.Exceptions;
using Atlas.Application.Common.Interfaces;
using Atlas.Application.Organizations.Dtos;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Atlas.Application.Tests.Auth;

/// <summary>
/// AuthService is the security-critical one — RegisterAsync/LoginAsync are the
/// only two anonymous endpoints in the whole API (see README's Authentication
/// section), so getting their failure paths right matters more here than
/// almost anywhere else in the codebase. These tests mock every dependency
/// (IUserRepository, IOrganizationRepository, IPasswordHasher,
/// IJwtTokenGenerator, IUnitOfWork, IAuditLogRepository) so each test is
/// about AuthService's own orchestration logic, never about a real
/// database, a real hash, or a real signed JWT.
/// </summary>
public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IOrganizationRepository> _organizationRepository = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<IJwtTokenGenerator> _jwtTokenGenerator = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAuditLogRepository> _auditLogRepository = new();

    private readonly Guid _organizationId = Guid.NewGuid();

    private AuthService CreateSut() => new(
        _userRepository.Object,
        _organizationRepository.Object,
        _passwordHasher.Object,
        _jwtTokenGenerator.Object,
        _unitOfWork.Object,
        _auditLogRepository.Object,
        NullLogger<AuthService>.Instance);

    [Fact]
    public async Task RegisterAsync_WithValidRequest_CreatesUserWritesAuditRowAndReturnsToken()
    {
        _userRepository.Setup(r => r.EmailExistsAsync("ada@northstar-it.example", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _passwordHasher.Setup(h => h.Hash("correct-horse-battery")).Returns("hashed-password");
        var expiresAtUtc = DateTime.UtcNow.AddHours(1);
        _jwtTokenGenerator.Setup(j => j.GenerateToken(It.IsAny<User>())).Returns(("a-signed-jwt", expiresAtUtc));

        var sut = CreateSut();
        var request = new RegisterRequest("Ada Admin", "ada@northstar-it.example", "correct-horse-battery", _organizationId, UserRole.Admin);

        var result = await sut.RegisterAsync(request, CancellationToken.None);

        Assert.Equal("a-signed-jwt", result.Token);
        Assert.Equal(expiresAtUtc, result.ExpiresAtUtc);
        Assert.Equal(UserRole.Admin, result.Role);

        // The user was actually persisted, with a hashed (never plaintext) password...
        _userRepository.Verify(r => r.AddAsync(
            It.Is<User>(u => u.Email == "ada@northstar-it.example" && u.PasswordHash == "hashed-password"),
            It.IsAny<CancellationToken>()), Times.Once);

        // ...an audit row was staged for the new user (Del 15) — self-registration is
        // the one case where the audited entity IS the actor (see AuthService's own
        // doc comment on RegisterAsync)...
        _auditLogRepository.Verify(r => r.AddAsync(
            It.Is<AuditLog>(a => a.Action == "Created" && a.EntityName == nameof(User)),
            It.IsAny<CancellationToken>()), Times.Once);

        // ...and never the plaintext or hashed password, even in the audit trail.
        _auditLogRepository.Verify(r => r.AddAsync(
            It.Is<AuditLog>(a => (a.NewValuesJson ?? string.Empty).Contains("hashed-password")),
            It.IsAny<CancellationToken>()), Times.Never);

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_WithPasswordShorterThan8Characters_ThrowsWithoutTouchingTheRepository()
    {
        var sut = CreateSut();
        var request = new RegisterRequest("Ada Admin", "ada@northstar-it.example", "short", _organizationId, UserRole.Admin);

        await Assert.ThrowsAsync<AuthenticationException>(() => sut.RegisterAsync(request, CancellationToken.None));

        _userRepository.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_WithAnEmailThatAlreadyExists_ThrowsWithoutTouchingTheRepository()
    {
        _userRepository.Setup(r => r.EmailExistsAsync("ada@northstar-it.example", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = CreateSut();
        var request = new RegisterRequest("Ada Admin", "ada@northstar-it.example", "correct-horse-battery", _organizationId, UserRole.Admin);

        await Assert.ThrowsAsync<AuthenticationException>(() => sut.RegisterAsync(request, CancellationToken.None));

        _userRepository.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WithCorrectPasswordAndActiveAccount_ReturnsToken()
    {
        var user = User.Create(_organizationId, "Ada Admin", "ada@northstar-it.example", UserRole.Admin);
        user.SetPassword("hashed-password");

        _userRepository.Setup(r => r.GetByEmailAsync("ada@northstar-it.example", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasher.Setup(h => h.Verify("correct-horse-battery", "hashed-password")).Returns(true);
        _jwtTokenGenerator.Setup(j => j.GenerateToken(user)).Returns(("a-signed-jwt", DateTime.UtcNow.AddHours(1)));

        var sut = CreateSut();
        var result = await sut.LoginAsync(new LoginRequest("ada@northstar-it.example", "correct-horse-battery"), CancellationToken.None);

        Assert.Equal("a-signed-jwt", result.Token);
    }

    [Fact]
    public async Task LoginAsync_WithUnknownEmail_ThrowsWithoutCallingThePasswordHasher()
    {
        _userRepository.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var sut = CreateSut();

        await Assert.ThrowsAsync<AuthenticationException>(
            () => sut.LoginAsync(new LoginRequest("nobody@example.test", "whatever12"), CancellationToken.None));

        // There's no password hash to check against for an email that doesn't
        // exist — verifying we never even try is what would catch a future
        // regression that dereferences a null user before the null-check.
        _passwordHasher.Verify(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ThrowsTheSameGenericMessageAsAnUnknownEmail()
    {
        var user = User.Create(_organizationId, "Ada Admin", "ada@northstar-it.example", UserRole.Admin);
        user.SetPassword("hashed-password");

        _userRepository.Setup(r => r.GetByEmailAsync("ada@northstar-it.example", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasher.Setup(h => h.Verify("wrong-password", "hashed-password")).Returns(false);

        var sut = CreateSut();

        // Deliberately asserting on the exact message here, not just the
        // exception type: AuthService's own doc comment is explicit that an
        // unknown email and a wrong password must be indistinguishable to the
        // caller (never reveal *which* one it was — that's a user-enumeration
        // vulnerability), so this is a regression test for that specific
        // security property, not just "it throws".
        var ex = await Assert.ThrowsAsync<AuthenticationException>(
            () => sut.LoginAsync(new LoginRequest("ada@northstar-it.example", "wrong-password"), CancellationToken.None));
        Assert.Equal("Invalid email or password.", ex.Message);
    }

    [Fact]
    public async Task LoginAsync_WithDeactivatedAccount_ThrowsEvenWithTheCorrectPassword()
    {
        var user = User.Create(_organizationId, "Ada Admin", "ada@northstar-it.example", UserRole.Admin);
        user.SetPassword("hashed-password");
        user.Deactivate();

        _userRepository.Setup(r => r.GetByEmailAsync("ada@northstar-it.example", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasher.Setup(h => h.Verify("correct-horse-battery", "hashed-password")).Returns(true);

        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<AuthenticationException>(
            () => sut.LoginAsync(new LoginRequest("ada@northstar-it.example", "correct-horse-battery"), CancellationToken.None));
        Assert.Equal("This account has been deactivated.", ex.Message);

        // Never even asked the JWT generator for a token — a deactivated
        // account has to fail before that point, not after.
        _jwtTokenGenerator.Verify(j => j.GenerateToken(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task GetRegistrableOrganizationsAsync_QueriesOnlyActiveOrganizationsAtMaxPageSizeAndMapsToTheMinimalDto()
    {
        var northstar = Organization.Create("Northstar IT", OrganizationType.Internal);
        var acme = Organization.Create("ACME AB", OrganizationType.Customer);

        OrganizationListQuery? capturedQuery = null;
        _organizationRepository
            .Setup(r => r.SearchAsync(It.IsAny<OrganizationListQuery>(), It.IsAny<CancellationToken>()))
            .Callback<OrganizationListQuery, CancellationToken>((q, _) => capturedQuery = q)
            .ReturnsAsync((new List<Organization> { northstar, acme }, 2));

        var sut = CreateSut();
        var result = await sut.GetRegistrableOrganizationsAsync(CancellationToken.None);

        // Deactivated organizations must never reach an anonymous caller, and
        // this is the entire dropdown's source, not one page of a longer
        // list — see the doc comment on GetRegistrableOrganizationsAsync.
        Assert.NotNull(capturedQuery);
        Assert.True(capturedQuery!.IsActive);
        Assert.Equal(OrganizationListQuery.MaxPageSize, capturedQuery.PageSize);

        // The minimal DTO only — never OrganizationDto's fuller shape (Type,
        // IsActive, CreatedAtUtc), consistent with OrganizationOptionDto's own
        // doc comment on why an anonymous caller sees only {id, name}.
        Assert.Equal(2, result.Count);
        Assert.Contains(result, o => o.Id == northstar.Id && o.Name == "Northstar IT");
        Assert.Contains(result, o => o.Id == acme.Id && o.Name == "ACME AB");
    }
}
