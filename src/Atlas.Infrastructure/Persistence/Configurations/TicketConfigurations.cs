using Atlas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atlas.Infrastructure.Persistence.Configurations;

public sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("Tickets", "dbo");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Description).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(t => t.Status).HasConversion<int>().IsRequired().HasDefaultValue(Domain.Enums.TicketStatus.New);
        builder.Property(t => t.Priority).HasConversion<int>().IsRequired().HasDefaultValue(Domain.Enums.TicketPriority.Medium);
        builder.Property(t => t.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(t => t.ModifiedAtUtc).HasColumnType("datetime2");
        builder.Property(t => t.DueAtUtc).HasColumnType("datetime2");
        builder.Property(t => t.ResolvedAtUtc).HasColumnType("datetime2");
        builder.Property(t => t.ClosedAtUtc).HasColumnType("datetime2");

        // Composite index matching the dashboard's most common query shape:
        // WHERE Status = @status ORDER BY CreatedAtUtc DESC (see sql/001_InitialSchema.sql
        // for the discussion of why this index exists — Del 9 revisits this).
        builder.HasIndex(t => new { t.Status, t.CreatedAtUtc }).HasDatabaseName("IX_Tickets_Status_CreatedAtUtc");
        builder.HasIndex(t => t.OrganizationId).HasDatabaseName("IX_Tickets_OrganizationId");
        builder.HasIndex(t => t.AssignedToUserId).HasDatabaseName("IX_Tickets_AssignedToUserId");
        builder.HasIndex(t => t.DueAtUtc).HasDatabaseName("IX_Tickets_DueAtUtc");

        builder.HasOne(t => t.Organization).WithMany().HasForeignKey(t => t.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.Project).WithMany().HasForeignKey(t => t.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.CreatedByUser).WithMany().HasForeignKey(t => t.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.AssignedToUser).WithMany().HasForeignKey(t => t.AssignedToUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.Comments).WithOne().HasForeignKey(c => c.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(t => t.History).WithOne().HasForeignKey(h => h.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(t => t.Tags).WithOne(tt => tt.Ticket!).HasForeignKey(tt => tt.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(t => t.Attachments).WithOne().HasForeignKey(a => a.TicketId).OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(t => t.Comments).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(t => t.History).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(t => t.Tags).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(t => t.Attachments).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class TicketCommentConfiguration : IEntityTypeConfiguration<TicketComment>
{
    public void Configure(EntityTypeBuilder<TicketComment> builder)
    {
        builder.ToTable("TicketComments", "dbo");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Body).HasMaxLength(4000).IsRequired();
        builder.Property(c => c.IsInternal).IsRequired();
        builder.Property(c => c.CreatedAtUtc).HasColumnType("datetime2").IsRequired();

        builder.HasIndex(c => c.TicketId).HasDatabaseName("IX_TicketComments_TicketId");

        builder.HasOne<User>().WithMany().HasForeignKey(c => c.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TicketHistoryConfiguration : IEntityTypeConfiguration<TicketHistory>
{
    public void Configure(EntityTypeBuilder<TicketHistory> builder)
    {
        builder.ToTable("TicketHistory", "dbo");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.FieldName).HasMaxLength(100).IsRequired();
        builder.Property(h => h.OldValue).HasMaxLength(1000);
        builder.Property(h => h.NewValue).HasMaxLength(1000);
        builder.Property(h => h.ChangedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(h => h.CreatedAtUtc).HasColumnType("datetime2").IsRequired();

        builder.HasIndex(h => new { h.TicketId, h.ChangedAtUtc }).HasDatabaseName("IX_TicketHistory_TicketId_ChangedAtUtc");

        builder.HasOne<User>().WithMany().HasForeignKey(h => h.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("Tags", "dbo");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
        builder.Property(t => t.CreatedAtUtc).HasColumnType("datetime2").IsRequired();

        builder.HasIndex(t => new { t.OrganizationId, t.Name }).IsUnique().HasDatabaseName("UX_Tags_OrganizationId_Name");

        builder.HasMany(t => t.TicketTags).WithOne(tt => tt.Tag!).HasForeignKey(tt => tt.TagId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(t => t.TicketTags).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class TicketTagConfiguration : IEntityTypeConfiguration<TicketTag>
{
    public void Configure(EntityTypeBuilder<TicketTag> builder)
    {
        builder.ToTable("TicketTags", "dbo");

        builder.HasKey(tt => tt.Id);

        builder.Property(tt => tt.CreatedAtUtc).HasColumnType("datetime2").IsRequired();

        builder.HasIndex(tt => new { tt.TicketId, tt.TagId }).IsUnique().HasDatabaseName("UX_TicketTags_TicketId_TagId");
    }
}

public sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("Attachments", "dbo");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.FileName).HasMaxLength(260).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.SizeInBytes).IsRequired();
        builder.Property(a => a.BlobName).HasMaxLength(500).IsRequired();
        builder.Property(a => a.CreatedAtUtc).HasColumnType("datetime2").IsRequired();

        builder.HasIndex(a => a.TicketId).HasDatabaseName("IX_Attachments_TicketId");

        builder.HasOne<User>().WithMany().HasForeignKey(a => a.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
