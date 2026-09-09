using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Atlas.Domain.Exceptions;
using Xunit;

namespace Atlas.Domain.Tests;

public class OrganizationTests
{
    [Fact]
    public void Create_WithValidName_TrimsIt()
    {
        var organization = Organization.Create("  ACME AB  ", OrganizationType.Customer);

        Assert.Equal("ACME AB", organization.Name);
        Assert.Equal(OrganizationType.Customer, organization.Type);
        Assert.True(organization.IsActive);
    }

    [Fact]
    public void Create_WithEmptyName_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() => Organization.Create(" ", OrganizationType.Internal));
    }

    [Fact]
    public void Deactivate_ThenReactivate_RestoresIsActive()
    {
        var organization = Organization.Create("Northstar IT", OrganizationType.Internal);

        organization.Deactivate();
        Assert.False(organization.IsActive);

        organization.Reactivate();
        Assert.True(organization.IsActive);
    }

    [Fact]
    public void Rename_WithValidName_TrimsAndUpdatesIt()
    {
        var organization = Organization.Create("ACME AB", OrganizationType.Customer);

        organization.Rename("  ACME Corp  ");

        Assert.Equal("ACME Corp", organization.Name);
    }

    [Fact]
    public void Rename_WithEmptyName_ThrowsDomainException()
    {
        var organization = Organization.Create("ACME AB", OrganizationType.Customer);

        Assert.Throws<DomainException>(() => organization.Rename(" "));
    }
}
