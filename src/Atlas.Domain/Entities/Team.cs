using Atlas.Domain.Common;
using Atlas.Domain.Exceptions;

namespace Atlas.Domain.Entities;

/// <summary>An internal team within an organization (e.g. "Support Tier 1", "Platform Engineering").</summary>
public sealed class Team : Entity
{
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = string.Empty;

    private readonly List<TeamMember> _members = [];
    public IReadOnlyCollection<TeamMember> Members => _members.AsReadOnly();

    private Team()
    {
        // Required by EF Core for materialization.
    }

    public static Team Create(Guid organizationId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Team name cannot be empty.");

        return new Team
        {
            OrganizationId = organizationId,
            Name = name.Trim()
        };
    }

    public TeamMember AddMember(Guid userId)
    {
        if (_members.Any(m => m.UserId == userId))
            throw new DomainException("User is already a member of this team.");

        var member = TeamMember.Create(Id, userId);
        _members.Add(member);
        MarkModified();
        return member;
    }

    public void RemoveMember(Guid userId)
    {
        var existing = _members.FirstOrDefault(m => m.UserId == userId);
        if (existing is null) return;
        _members.Remove(existing);
        MarkModified();
    }
}

/// <summary>Join entity linking a <see cref="User"/> to a <see cref="Team"/> (many-to-many).</summary>
public sealed class TeamMember : Entity
{
    public Guid TeamId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTime JoinedAtUtc { get; private set; }

    private TeamMember()
    {
        // Required by EF Core for materialization.
    }

    internal static TeamMember Create(Guid teamId, Guid userId) =>
        new()
        {
            TeamId = teamId,
            UserId = userId,
            JoinedAtUtc = DateTime.UtcNow
        };
}
