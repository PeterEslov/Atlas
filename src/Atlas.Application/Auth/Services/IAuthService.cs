using Atlas.Application.Auth.Dtos;

namespace Atlas.Application.Auth.Services;

public interface IAuthService
{
    Task<AuthResponseDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    Task<AuthResponseDto> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    /// <summary>Active organizations only, for the Register form's dropdown — see OrganizationOptionDto's doc comment.</summary>
    Task<IReadOnlyList<OrganizationOptionDto>> GetRegistrableOrganizationsAsync(CancellationToken cancellationToken);
}
