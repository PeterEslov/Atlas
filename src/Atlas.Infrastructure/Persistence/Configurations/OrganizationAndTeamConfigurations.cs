using Atlas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atlas.Infrastructure.Persistence.Configurations;

public sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations", "dbo");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Name).HasMaxLength(200).IsRequired();
        builder.Property(o => o.Type).HasConversion<int>().IsRequired();
        builder.Property(o => o.IsActive).IsRequired();
        builder.Property(o => o.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(o => o.ModifiedAtUtc).HasColumnType("datetime2");

        builder.HasIndex(o => o.Name).HasDatabaseName("IX_Organizations_Name");

        builder.HasMany(o => o.Users).WithOne().HasForeignKey(u => u.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(o => o.Teams).WithOne().HasForeignKey(t => t.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(o => o.Projects).WithOne().HasForeignKey(p => p.OrganizationId).OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(o => o.Users).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(o => o.Teams).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(o => o.Projects).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users", "dbo");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.FullName).HasMaxLength(200).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(256).IsRequired();
        builder.Property(u => u.Role).HasConversion<int>().IsRequired();
        builder.Property(u => u.IsActive).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(500);
        builder.Property(u => u.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(u => u.ModifiedAtUtc).HasColumnType("datetime2");

        builder.HasIndex(u => u.Email).IsUnique().HasDatabaseName("UX_Users_Email");
        builder.HasIndex(u => u.OrganizationId).HasDatabaseName("IX_Users_OrganizationId");

        builder.HasMany(u => u.TeamMemberships).WithOne().HasForeignKey(tm => tm.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(u => u.TeamMemberships).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("Teams", "dbo");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(t => t.ModifiedAtUtc).HasColumnType("datetime2");

        builder.HasIndex(t => t.OrganizationId).HasDatabaseName("IX_Teams_OrganizationId");

        builder.HasMany(t => t.Members).WithOne().HasForeignKey(m => m.TeamId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Members).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class TeamMemberConfiguration : IEntityTypeConfiguration<TeamMember>
{
    public void Configure(EntityTypeBuilder<TeamMember> builder)
    {
        builder.ToTable("TeamMembers", "dbo");

        builder.HasKey(tm => tm.Id);

        builder.Property(tm => tm.JoinedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(tm => tm.CreatedAtUtc).HasColumnType("datetime2").IsRequired();

        builder.HasIndex(tm => new { tm.TeamId, tm.UserId }).IsUnique().HasDatabaseName("UX_TeamMembers_TeamId_UserId");
    }
}
