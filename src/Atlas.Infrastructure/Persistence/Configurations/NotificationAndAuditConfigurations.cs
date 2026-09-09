using Atlas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atlas.Infrastructure.Persistence.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications", "dbo");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Type).HasConversion<int>().IsRequired();
        builder.Property(n => n.Message).HasMaxLength(1000).IsRequired();
        builder.Property(n => n.IsRead).IsRequired();
        builder.Property(n => n.ReadAtUtc).HasColumnType("datetime2");
        builder.Property(n => n.CreatedAtUtc).HasColumnType("datetime2").IsRequired();

        builder.HasIndex(n => new { n.RecipientUserId, n.IsRead }).HasDatabaseName("IX_Notifications_RecipientUserId_IsRead");

        builder.HasOne<User>().WithMany().HasForeignKey(n => n.RecipientUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Ticket>().WithMany().HasForeignKey(n => n.RelatedTicketId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs", "dbo");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Action).HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityName).HasMaxLength(100).IsRequired();
        builder.Property(a => a.OldValuesJson).HasColumnType("nvarchar(max)");
        builder.Property(a => a.NewValuesJson).HasColumnType("nvarchar(max)");
        builder.Property(a => a.TimestampUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(a => a.CreatedAtUtc).HasColumnType("datetime2").IsRequired();

        builder.HasIndex(a => new { a.EntityName, a.EntityId }).HasDatabaseName("IX_AuditLogs_EntityName_EntityId");
        builder.HasIndex(a => a.TimestampUtc).HasDatabaseName("IX_AuditLogs_TimestampUtc");

        builder.HasOne<User>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
