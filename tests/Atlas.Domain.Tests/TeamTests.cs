using Atlas.Domain.Entities;
using Atlas.Domain.Exceptions;
using Xunit;

namespace Atlas.Domain.Tests;

public class TeamTests
{
    [Fact]
    public void Create_WithValidName_TrimsIt()
    {
        var organizationId = Guid.NewGuid();

        var team = Team.Create(organizationId, "  Support Tier 1  ");

        Assert.Equal(organizationId, team.OrganizationId);
        Assert.Equal("Support Tier 1", team.Name);
        Assert.Empty(team.Members);
    }

    [Fact]
    public void Create_WithEmptyName_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() => Team.Create(Guid.NewGuid(), " "));
    }

    [Fact]
    public void AddMember_WithNewUser_AddsThem()
    {
        var team = Team.Create(Guid.NewGuid(), "Support Tier 1");
        var userId = Guid.NewGuid();

        var member = team.AddMember(userId);

        Assert.Equal(userId, member.UserId);
        Assert.Equal(team.Id, member.TeamId);
        Assert.Single(team.Members);
    }

    [Fact]
    public void AddMember_SameUserTwice_ThrowsDomainException()
    {
        var team = Team.Create(Guid.NewGuid(), "Support Tier 1");
        var userId = Guid.NewGuid();
        team.AddMember(userId);

        Assert.Throws<DomainException>(() => team.AddMember(userId));
    }

    [Fact]
    public void RemoveMember_ExistingMember_RemovesThem()
    {
        var team = Team.Create(Guid.NewGuid(), "Support Tier 1");
        var userId = Guid.NewGuid();
        team.AddMember(userId);

        team.RemoveMember(userId);

        Assert.Empty(team.Members);
    }

    [Fact]
    public void RemoveMember_UnknownUser_IsANoOp()
    {
        var team = Team.Create(Guid.NewGuid(), "Support Tier 1");

        // Deliberately doesn't throw — removing someone who isn't a member
        // already gets you to the state you wanted, so there's no invariant
        // to protect here. Contrast with AddMember, which does throw on a
        // duplicate, because silently ignoring that could hide a caller bug.
        var exception = Record.Exception(() => team.RemoveMember(Guid.NewGuid()));

        Assert.Null(exception);
    }
}
