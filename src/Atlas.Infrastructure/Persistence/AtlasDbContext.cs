using System.Reflection;
using Atlas.Application.Common.Interfaces;
using Atlas.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for Project Atlas, targeting Azure SQL / SQL Server via T-SQL.
/// Table shape (keys, indexes, relationships, column types) is defined entirely by
/// the IEntityTypeConfiguration&lt;T&gt; classes in Persistence/Configurations — see
/// sql/001_InitialSchema.sql for the equivalent hand-written T-SQL reference.
/// </summary>
public sealed class AtlasDbContext : DbContext, IUnitOfWork
{
    public AtlasDbContext(DbContextOptions<AtlasDbContext> options) : base(options)
    {
    }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketComment> TicketComments => Set<TicketComment>();
    public DbSet<TicketHistory> TicketHistories => Set<TicketHistory>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<TicketTag> TicketTags => Set<TicketTag>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Every table gets its shape from its own IEntityTypeConfiguration<T> in
        // this assembly — keeps OnModelCreating itself a one-liner as the model grows.
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        base.OnModelCreating(modelBuilder);
    }
}
