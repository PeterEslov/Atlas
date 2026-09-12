using System.Net.Http.Headers;
using System.Net.Http.Json;
using Atlas.Application.Auth.Dtos;
using Atlas.Domain.Enums;

namespace Atlas.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Every integration test that needs an authenticated caller goes through the
/// real POST /api/auth/register + the token it returns — never by hand-
/// crafting a JWT — for the same "prefer a real local equivalent over a fake"
/// reasoning behind LocalDB/Azurite/a local Redis container elsewhere in this
/// project. Registering through the real endpoint also means these tests
/// incidentally re-exercise AuthService.RegisterAsync and JwtTokenGenerator on
/// every single run, for free.
/// </summary>
public static class AuthTestHelper
{
    /// <summary>A password meeting AuthService's only rule (at least 8 characters) — see AuthServiceTests.</summary>
    public const string Password = "Integration123!";

    public static async Task<AuthResponseDto> RegisterAsync(
        HttpClient client,
        Guid organizationId,
        UserRole role,
        string? email = null)
    {
        email ??= $"{Guid.NewGuid():N}@integration.test";
        var request = new RegisterRequest($"Integration {role}", email, Password, organizationId, role);

        var response = await client.PostAsJsonAsync("/api/auth/register", request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        return result ?? throw new InvalidOperationException("POST /api/auth/register returned 2xx with no body.");
    }

    /// <summary>
    /// Registers a brand-new user with the given role in the given
    /// organization (defaulting to ApiFactory.SeededOrganizationId) and
    /// returns an HttpClient with that user's bearer token already attached
    /// to every request — the shape almost every test in this project wants.
    /// </summary>
    public static async Task<HttpClient> CreateAuthenticatedClientAsync(
        this ApiFactory factory,
        UserRole role,
        Guid? organizationId = null,
        string? email = null)
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, organizationId ?? factory.SeededOrganizationId, role, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return client;
    }
}
