using Atlas.Domain.Entities;

namespace Atlas.Application.Common.Interfaces;

/// <summary>Issues a signed JWT for an authenticated user, with role and permission claims baked in.</summary>
public interface IJwtTokenGenerator
{
    (string Token, DateTime ExpiresAtUtc) GenerateToken(User user);
}
