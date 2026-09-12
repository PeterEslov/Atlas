using Atlas.Application.Auth.Dtos;
using Atlas.Application.Common;
using Atlas.Application.Common.Exceptions;
using Atlas.Application.Common.Interfaces;
using Atlas.Domain.Entities;
using Atlas.Domain.Security;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Auth.Services;

/// <summary>Registration and login. Password hashing and JWT signing are delegated to infrastructure via interfaces.</summary>
public sealed class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        IUnitOfWork unitOfWork,
        IAuditLogRepository auditLogRepository,
        ILogger<AuthService> logger)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _unitOfWork = unitOfWork;
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task<AuthResponseDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        {
            throw new AuthenticationException("Password must be at least 8 characters long.");
        }

        if (await _userRepository.EmailExistsAsync(request.Email, cancellationToken))
        {
            throw new AuthenticationException($"An account with email '{request.Email}' already exists.");
        }

        var user = User.Create(request.OrganizationId, request.FullName, request.Email, request.Role);
        user.SetPassword(_passwordHasher.Hash(request.Password));

        await _userRepository.AddAsync(user, cancellationToken);

        // The audited entity IS the actor here — self-registration has no
        // separate "who did this to whom", unlike e.g. UserService.ChangeRoleAsync
        // below, where an admin acts on someone else. Password/hash is never
        // part of NewValuesJson, on the same principle TicketHistory already
        // follows for any field (never store secrets in a free-form audit blob
        // that a wider set of people can read than can read the User table itself).
        var auditLog = AuditLog.Create(
            user.Id,
            "Created",
            nameof(User),
            user.Id,
            oldValuesJson: null,
            newValuesJson: AuditLogSerializer.ToJson(new { fullName = user.FullName, email = user.Email, role = user.Role, organizationId = user.OrganizationId }));
        await _auditLogRepository.AddAsync(auditLog, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {UserId} registered with role {Role}", user.Id, user.Role);
        return BuildAuthResponse(user);
    }

    public async Task<AuthResponseDto> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);

        if (user is null || user.PasswordHash is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            // Same message whether the email doesn't exist or the password is wrong —
            // never reveal which one it was, that's a user-enumeration vulnerability.
            throw new AuthenticationException("Invalid email or password.");
        }

        if (!user.IsActive)
        {
            throw new AuthenticationException("This account has been deactivated.");
        }

        _logger.LogInformation("User {UserId} logged in", user.Id);
        return BuildAuthResponse(user);
    }

    private AuthResponseDto BuildAuthResponse(User user)
    {
        var (token, expiresAtUtc) = _jwtTokenGenerator.GenerateToken(user);
        var permissions = RolePermissions.For(user.Role).ToList();

        return new AuthResponseDto(token, expiresAtUtc, user.Id, user.FullName, user.Email, user.Role, permissions);
    }
}
