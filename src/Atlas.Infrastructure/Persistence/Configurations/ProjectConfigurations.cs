using Atlas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atlas.Infrastructure.Persistence.Configurations;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects", "dbo");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(2000).IsRequired();
        builder.Property(p => p.IsArchived).IsRequired();
        builder.Property(p => p.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(p => p.ModifiedAtUtc).HasColumnType("datetime2");

        builder.HasIndex(p => p.OrganizationId).HasDatabaseName("IX_Projects_OrganizationId");

        builder.HasMany(p => p.Members).WithOne().HasForeignKey(m => m.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Members).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> builder)
    {
        builder.ToTable("ProjectMembers", "dbo");

        builder.HasKey(pm => pm.Id);

        builder.Property(pm => pm.JoinedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(pm => pm.CreatedAtUtc).HasColumnType("datetime2").IsRequired();

        builder.HasIndex(pm => new { pm.ProjectId, pm.UserId }).IsUnique().HasDatabaseName("UX_ProjectMembers_ProjectId_UserId");

        // UserId is a plain FK column here (no navigation exposed) to keep User free
        // of a ProjectMemberships collection it doesn't need for any current use case.
        builder.HasOne<User>().WithMany().HasForeignKey(pm => pm.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
