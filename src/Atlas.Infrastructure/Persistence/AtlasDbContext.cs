using System.Reflection;
using Atlas.Application.Common.Interfaces;
using Atlas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

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

        // Every entity in this model assigns its own Guid Id itself, in its
        // constructor (Guid.NewGuid()) — none of them are database-generated.
        // Left unconfigured, EF Core's default convention for a Guid primary
        // key is still ValueGeneratedOnAdd (it *could* be store-generated), and
        // that default is what creates a real ambiguity the moment a *new*
        // child entity is discovered through change tracking rather than an
        // explicit Add() call — e.g. a domain method doing
        // "_history.Add(new TicketHistory(...))" on a Ticket that's already
        // tracked (loaded via GetByIdAsync, not freshly created). EF sees a
        // non-default key value on an entity it has never tracked before and,
        // since that kind of key *could* be store-generated, assumes it must
        // already exist in the database and marks it Modified instead of
        // Added — producing exactly the "expected to affect 1 row(s), but
        // actually affected 0" DbUpdateConcurrencyException Del 10 first hit
        // (for Attachment, via TicketService.AddAttachmentAsync — worked
        // around there with an explicit repository Add() call) and Del 11 hit
        // again (for TicketHistory, via Ticket.AssignTo). Marking every Guid
        // key ValueGeneratedNever() tells EF Core the truth — this key is
        // never store-generated, full stop — which removes the ambiguity at
        // its source, for every entity, instead of patching each call site
        // one at a time as the same bug resurfaces for the next collection.Add().
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var idProperty = entityType.FindProperty("Id");
            if (idProperty is not null && idProperty.ClrType == typeof(Guid))
            {
                idProperty.ValueGenerated = ValueGenerated.Never;
            }
        }

        base.OnModelCreating(modelBuilder);
    }
}
