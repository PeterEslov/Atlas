using Atlas.Domain.Common;
using Atlas.Domain.Enums;
using Atlas.Domain.Exceptions;

namespace Atlas.Domain.Entities;

/// <summary>
/// An organization using the platform. A single table covers both the internal
/// company (e.g. "Northstar IT", <see cref="OrganizationType.Internal"/>) and its
/// customers (e.g. "ACME AB", <see cref="OrganizationType.Customer"/>) since they
/// share every structural property — only the type differs.
/// </summary>
public sealed class Organization : Entity
{
    public string Name { get; private set; } = string.Empty;
    public OrganizationType Type { get; private set; }
    public bool IsActive { get; private set; } = true;

    private readonly List<User> _users = [];
    public IReadOnlyCollection<User> Users => _users.AsReadOnly();

    private readonly List<Team> _teams = [];
    public IReadOnlyCollection<Team> Teams => _teams.AsReadOnly();

    private readonly List<Project> _projects = [];
    public IReadOnlyCollection<Project> Projects => _projects.AsReadOnly();

    private Organization()
    {
        // Required by EF Core for materialization.
    }

    public static Organization Create(string name, OrganizationType type)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Organization name cannot be empty.");

        return new Organization
        {
            Name = name.Trim(),
            Type = type
        };
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

    public void Rename(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new DomainException("Organization name cannot be empty.");

        Name = newName.Trim();
        MarkModified();
    }
}
