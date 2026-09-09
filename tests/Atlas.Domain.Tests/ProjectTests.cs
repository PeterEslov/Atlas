using Atlas.Domain.Entities;
using Atlas.Domain.Exceptions;
using Xunit;

namespace Atlas.Domain.Tests;

public class ProjectTests
{
    [Fact]
    public void Create_WithValidNameAndDescription_TrimsBoth()
    {
        var organizationId = Guid.NewGuid();

        var project = Project.Create(organizationId, "  ACME Onboarding Q3  ", "  Kickoff through go-live  ");

        Assert.Equal(organizationId, project.OrganizationId);
        Assert.Equal("ACME Onboarding Q3", project.Name);
        Assert.Equal("Kickoff through go-live", project.Description);
        Assert.False(project.IsArchived);
        Assert.Empty(project.Members);
    }

    [Fact]
    public void Create_WithoutDescription_DefaultsToEmptyString()
    {
        var project = Project.Create(Guid.NewGuid(), "Internal Tooling");

        Assert.Equal(string.Empty, project.Description);
    }

    [Fact]
    public void Create_WithEmptyName_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() => Project.Create(Guid.NewGuid(), " "));
    }

    [Fact]
    public void AddMember_WithNewUser_AddsThem()
    {
        var project = Project.Create(Guid.NewGuid(), "ACME Onboarding Q3");
        var userId = Guid.NewGuid();

        var member = project.AddMember(userId);

        Assert.Equal(userId, member.UserId);
        Assert.Equal(project.Id, member.ProjectId);
        Assert.Single(project.Members);
    }

    [Fact]
    public void AddMember_SameUserTwice_ThrowsDomainException()
    {
        var project = Project.Create(Guid.NewGuid(), "ACME Onboarding Q3");
        var userId = Guid.NewGuid();
        project.AddMember(userId);

        // A project has no RemoveMember (unlike Team) — see the doc comment on
        // ProjectService for why that's a known, deliberate gap for now.
        Assert.Throws<DomainException>(() => project.AddMember(userId));
    }

    [Fact]
    public void Archive_SetsIsArchivedTrue()
    {
        var project = Project.Create(Guid.NewGuid(), "ACME Onboarding Q3");

        project.Archive();

        Assert.True(project.IsArchived);
    }
}
