using Atlas.Domain.Enums;
using Atlas.Domain.Security;
using Xunit;

namespace Atlas.Domain.Tests;

public class RolePermissionsTests
{
    [Fact]
    public void Admin_HasEveryPermission()
    {
        var adminPermissions = RolePermissions.For(UserRole.Admin);

        Assert.Equal(Permissions.All.Count, adminPermissions.Count);
        Assert.All(Permissions.All, permission => Assert.Contains(permission, adminPermissions));
    }

    [Fact]
    public void Customer_CanReadAndCreateTickets_ButCannotAssignOrDeleteThem()
    {
        var customerPermissions = RolePermissions.For(UserRole.Customer);

        Assert.Contains(Permissions.TicketRead, customerPermissions);
        Assert.Contains(Permissions.TicketCreate, customerPermissions);
        Assert.DoesNotContain(Permissions.TicketAssign, customerPermissions);
        Assert.DoesNotContain(Permissions.TicketUpdate, customerPermissions);
        Assert.DoesNotContain(Permissions.TicketDelete, customerPermissions);
    }

    [Fact]
    public void Agent_CanAssignAndUpdateTickets_ButCannotDeleteThem()
    {
        var agentPermissions = RolePermissions.For(UserRole.Agent);

        Assert.Contains(Permissions.TicketUpdate, agentPermissions);
        Assert.Contains(Permissions.TicketAssign, agentPermissions);
        Assert.DoesNotContain(Permissions.TicketDelete, agentPermissions);
    }

    [Fact]
    public void Manager_CanDeleteTicketsManageProjectsAndManageUsers_ButCannotManageOrganizations()
    {
        var managerPermissions = RolePermissions.For(UserRole.Manager);

        Assert.Contains(Permissions.TicketDelete, managerPermissions);
        Assert.Contains(Permissions.ProjectManage, managerPermissions);
        Assert.Contains(Permissions.UserRead, managerPermissions);
        Assert.Contains(Permissions.UserManage, managerPermissions);
        Assert.Contains(Permissions.OrganizationRead, managerPermissions);
        Assert.DoesNotContain(Permissions.OrganizationManage, managerPermissions);
    }

    [Theory]
    [InlineData(UserRole.Customer)]
    [InlineData(UserRole.Agent)]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.Admin)]
    public void EveryRole_IsGrantedAtLeastTicketRead(UserRole role)
    {
        Assert.Contains(Permissions.TicketRead, RolePermissions.For(role));
    }
}
