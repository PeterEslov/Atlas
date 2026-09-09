namespace Atlas.Infrastructure.Security;

/// <summary>
/// Bound from the "Jwt" configuration section (appsettings.Development.json for
/// local dev). SigningKey must be at least 32 characters (256 bits) for HS256.
/// In Azure this moves to Key Vault via DefaultAzureCredential — see Del 18 —
/// never committed as a real secret past local development.
/// </summary>
public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "ProjectAtlas";
    public string Audience { get; set; } = "ProjectAtlas.Api";
    public string SigningKey { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; } = 60;
}
