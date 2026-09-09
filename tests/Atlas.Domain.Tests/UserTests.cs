using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Atlas.Domain.Exceptions;
using Xunit;

namespace Atlas.Domain.Tests;

public class UserTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();

    private static User CreateUser(UserRole role = UserRole.Customer) =>
        User.Create(OrganizationId, "Ada Lovelace", "ada@northstar-it.example", role);

    [Fact]
    public void Create_WithValidData_LeavesPasswordHashNull()
    {
        var user = CreateUser();

        Assert.Null(user.PasswordHash);
    }

    [Fact]
    public void Create_NormalizesEmailToLowercaseAndTrimsFullName()
    {
        var user = User.Create(OrganizationId, "  Ada Lovelace  ", "Ada@Northstar-IT.example", UserRole.Agent);

        Assert.Equal("Ada Lovelace", user.FullName);
        Assert.Equal("ada@northstar-it.example", user.Email);
    }

    [Fact]
    public void Create_WithInvalidEmail_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() =>
            User.Create(OrganizationId, "Ada Lovelace", "not-an-email", UserRole.Customer));
    }

    [Fact]
    public void SetPassword_WithNonEmptyHash_StoresIt()
    {
        var user = CreateUser();

        // Domain never hashes passwords itself (see IPasswordHasher) — any
        // opaque non-empty string stands in for a real hash here.
        user.SetPassword("100000.c29tZXNhbHQ=.c29tZWhhc2g=");

        Assert.Equal("100000.c29tZXNhbHQ=.c29tZWhhc2g=", user.PasswordHash);
    }

    [Fact]
    public void SetPassword_WithEmptyHash_ThrowsDomainException()
    {
        var user = CreateUser();

        Assert.Throws<DomainException>(() => user.SetPassword(" "));
    }

    [Fact]
    public void Deactivate_ThenReactivate_RestoresIsActive()
    {
        var user = CreateUser();

        user.Deactivate();
        Assert.False(user.IsActive);

        user.Reactivate();
        Assert.True(user.IsActive);
    }
}
