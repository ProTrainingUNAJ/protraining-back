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
    // EF lo vuelve a leer en cada consulta, por lo que siempre refleja el PF de la petición actual.
    // Sin token es Guid.Empty y los filtros no devuelven nada.
    private Guid CurrentPfId => currentUser.PfId ?? Guid.Empty;

    public DbSet<CatalogTest> CatalogTests { get; set; }
    public DbSet<CheckIn> CheckIns { get; set; }
    public DbSet<AthleteInjury> AthleteInjuries { get; set; }
    public DbSet<AthleteInjuryStatusChange> AthleteInjuryStatusChanges { get; set; }
    public DbSet<DailyLoad> DailyLoads { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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

    // Para cada entidad que hereda de BaseEntity: columnas comunes, índice por PF
    // y el filtro global (solo los datos del PF del token, nunca los registros eliminados).
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

    // CatalogTest con PfId = Guid.Empty es un catálogo global: se ve desde cualquier PF.
    private Expression<Func<T, bool>> FilterByPf<T>() where T : BaseEntity =>
        typeof(T) == typeof(CatalogTest)
        ? entity => (entity.PfId == CurrentPfId || entity.PfId == Guid.Empty) && entity.DeletedAt == null
        : entity => entity.PfId == CurrentPfId && entity.DeletedAt == null;

    // Al crear: PfId desde el token y CreatedAt. Al eliminar: borrado lógico (DeletedAt).
    // PfId y CreatedAt no pueden modificarse después.
    private void SetBaseFields()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    // CatalogTest se reserva el PfId = Guid.Empty para el catálogo global (seeder sin token).
                    entry.Property(entity => entity.PfId).CurrentValue = currentUser.PfId
                        ?? (entry.Entity is CatalogTest ? Guid.Empty : throw new UnauthorizedAccessException("No se puede guardar sin un PF en el token."));
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
