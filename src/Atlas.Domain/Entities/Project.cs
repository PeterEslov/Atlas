using Atlas.Domain.Common;
using Atlas.Domain.Exceptions;

namespace Atlas.Domain.Entities;

/// <summary>A body of work tickets can optionally belong to, e.g. "ACME Onboarding Q3".</summary>
public sealed class Project : Entity
{
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public bool IsArchived { get; private set; }

    private readonly List<ProjectMember> _members = [];
    public IReadOnlyCollection<ProjectMember> Members => _members.AsReadOnly();

    private Project()
    {
        // Required by EF Core for materialization.
    }

    public static Project Create(Guid organizationId, string name, string description = "")
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Project name cannot be empty.");

        return new Project
        {
            OrganizationId = organizationId,
            Name = name.Trim(),
            Description = description.Trim()
        };
    }

    public ProjectMember AddMember(Guid userId)
    {
        if (_members.Any(m => m.UserId == userId))
            throw new DomainException("User is already a member of this project.");

        var member = ProjectMember.Create(Id, userId);
        _members.Add(member);
        MarkModified();
        return member;
    }

    public void Archive()
    {
        IsArchived = true;
        MarkModified();
    }
}

/// <summary>Join entity linking a <see cref="User"/> to a <see cref="Project"/> (many-to-many).</summary>
public sealed class ProjectMember : Entity
{
    public Guid ProjectId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTime JoinedAtUtc { get; private set; }

    private ProjectMember()
    {
        // Required by EF Core for materialization.
    }

    internal static ProjectMember Create(Guid projectId, Guid userId) =>
        new()
        {
            ProjectId = projectId,
            UserId = userId,
            JoinedAtUtc = DateTime.UtcNow
        };
}
