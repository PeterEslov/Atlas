using Atlas.Domain.Common;
using Atlas.Domain.Enums;
using Atlas.Domain.Exceptions;

namespace Atlas.Domain.Entities;

/// <summary>
/// A person who can sign in to Atlas: a customer contact, a support agent, a
/// manager or an admin, distinguished by <see cref="Role"/>. <see cref="PasswordHash"/>
/// is an opaque string produced by an <c>IPasswordHasher</c> in the application/
/// infrastructure layers — this entity never hashes or verifies passwords itself,
/// it only stores the result, keeping Atlas.Domain free of any crypto dependency.
/// Swapping to Azure Entra ID later (Del 20) means this column simply stops being
/// written; nothing else about the entity needs to change.
/// </summary>
public sealed class User : Entity
{
    public Guid OrganizationId { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public UserRole Role { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string? PasswordHash { get; private set; }

    /// <summary>
    /// Navigation property, populated by EF Core only when explicitly requested
    /// via .Include(...) — null otherwise. Added in Del 6 alongside the
    /// Organizations &amp; Users management endpoints, mirroring the pattern
    /// Ticket already uses for its own Organization reference; it required no
    /// migration since the OrganizationId foreign key column already existed —
    /// this just tells EF Core how to *load* the related row, it doesn't change
    /// what's stored.
    /// </summary>
    public Organization? Organization { get; private set; }

    private readonly List<TeamMember> _teamMemberships = [];
    public IReadOnlyCollection<TeamMember> TeamMemberships => _teamMemberships.AsReadOnly();

    private User()
    {
        // Required by EF Core for materialization.
    }

    public static User Create(Guid organizationId, string fullName, string email, UserRole role)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new DomainException("User full name cannot be empty.");

        if (!IsValidEmail(email))
            throw new DomainException($"'{email}' is not a valid email address.");

        return new User
        {
            OrganizationId = organizationId,
            FullName = fullName.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            Role = role
        };
    }

    /// <summary>Stores an already-hashed password. Hashing itself is the caller's job (see IPasswordHasher).</summary>
    public void SetPassword(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new DomainException("Password hash cannot be empty.");

        PasswordHash = passwordHash;
        MarkModified();
    }

    public void ChangeRole(UserRole newRole)
    {
        Role = newRole;
        MarkModified();
    }

    public void Deactivate()
    {
        IsActive = false;
        MarkModified();
    }

    public void Reactivate()
    {
        IsActive = true;
        MarkModified();
    }

    private static bool IsValidEmail(string email) =>
        !string.IsNullOrWhiteSpace(email)
        && email.Contains('@')
        && email.IndexOf('@') > 0
        && email.IndexOf('@') < email.Length - 1;
}
