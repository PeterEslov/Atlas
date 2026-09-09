using Atlas.Application.Auth.Dtos;

namespace Atlas.Application.Auth.Services;

public interface IAuthService
{
    Task<AuthResponseDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    Task<AuthResponseDto> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
}
