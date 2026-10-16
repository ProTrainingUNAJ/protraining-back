using System.Linq.Expressions;
using System.Reflection;
using Application.Interfaces.Services;
using Domain.Common;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
    : DbContext(options)
{
    // EF reads it again on every query, so it is always the PF of the current request.
    // Without a token it is Guid.Empty and the filters return nothing.
    private Guid CurrentPfId => currentUser.PfId ?? Guid.Empty;

    public DbSet<Exercise> Exercises { get; set; }

    public DbSet<AthleteRecord> AthleteRecords { get; set; }

    public DbSet<TrainingPlan> TrainingPlans => Set<TrainingPlan>();
    public DbSet<PlanAssignment> PlanAssignments => Set<PlanAssignment>();
    public DbSet<TrainingWeek> TrainingWeeks => Set<TrainingWeek>();
    public DbSet<TrainingSession> TrainingSessions => Set<TrainingSession>();
    public DbSet<PrescribedExercise> PrescribedExercises => Set<PrescribedExercise>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        ConfigureBaseEntities(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        SetBaseFields();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        SetBaseFields();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // For every entity that inherits from BaseEntity: common columns, index by PF
    // and the global filter (only the token's PF data, never the deleted rows).
    private void ConfigureBaseEntities(ModelBuilder modelBuilder)
    {
        var types = modelBuilder.Model.GetEntityTypes()
            .Where(type => typeof(BaseEntity).IsAssignableFrom(type.ClrType) && type.BaseType is null)
            .Select(type => type.ClrType)
            .ToList();

        var createFilter = typeof(AppDbContext).GetMethod(nameof(FilterByPf), BindingFlags.NonPublic | BindingFlags.Instance)!;

        foreach (var type in types)
        {
            var entity = modelBuilder.Entity(type);
            entity.HasKey(nameof(BaseEntity.Id));
            entity.Property(nameof(BaseEntity.Id)).HasColumnName("id").ValueGeneratedNever();
            entity.Property(nameof(BaseEntity.PfId)).HasColumnName("pf_id").IsRequired();
            entity.Property(nameof(BaseEntity.CreatedAt)).HasColumnName("created_at").IsRequired();
            entity.Property(nameof(BaseEntity.DeletedAt)).HasColumnName("deleted_at");
            entity.HasIndex(nameof(BaseEntity.PfId));
            entity.HasQueryFilter((LambdaExpression)createFilter.MakeGenericMethod(type).Invoke(this, null)!);
        }
    }

    private Expression<Func<T, bool>> FilterByPf<T>() where T : BaseEntity =>
        typeof(T) == typeof(Exercise)
        ? entity => (entity.PfId == CurrentPfId || entity.PfId == Guid.Empty) && entity.DeletedAt == null
        : entity => entity.PfId == CurrentPfId && entity.DeletedAt == null;

    // On create: PfId from the token and CreatedAt. On delete: soft delete (DeletedAt).
    // PfId and CreatedAt cannot be changed afterwards.
    private void SetBaseFields()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(entity => entity.PfId).CurrentValue = currentUser.PfId
                        ?? (entry.Entity is Exercise ? Guid.Empty : throw new UnauthorizedAccessException("No se puede guardar sin un PF en el token."));
                    entry.Property(entity => entity.CreatedAt).CurrentValue = now;
                    break;

                case EntityState.Modified:
                    entry.Property(entity => entity.PfId).IsModified = false;
                    entry.Property(entity => entity.CreatedAt).IsModified = false;
                    break;

                case EntityState.Deleted:
                    entry.State = EntityState.Modified;
                    entry.Property(entity => entity.DeletedAt).CurrentValue = now;
                    break;
            }
        }
    }
}
