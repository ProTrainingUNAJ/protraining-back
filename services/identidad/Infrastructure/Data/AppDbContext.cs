using System.Linq.Expressions;
using System.Reflection;
using Application.Interfaces.Services;
using Domain.Common;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, IUsuarioActual usuarioActual)
    : IdentityDbContext<User, IdentityRole<Guid>, Guid>(options)
{
    // EF lo vuelve a leer en cada consulta, así que siempre es el PF del pedido actual.
    // Sin token es Guid.Empty y los filtros no devuelven nada.
    private Guid PfIdActual => usuarioActual.PfId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
             // 1. obligatorio para identity: carga sus claves primarias
        base.OnModelCreating(modelBuilder);
        
             // 2. esquema "db_identity"
        modelBuilder.HasDefaultSchema("db_identity");
        
             // 3. nombres de tablas de identity en minusculas
        modelBuilder.Entity<User>(b =>
             {
            b.ToTable("usuarios");
            b.Property(u => u.FullName).HasMaxLength(200);
                 });
        modelBuilder.Entity<IdentityRole<Guid>>().ToTable("roles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("usuarios_roles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("usuarios_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("usuarios_logins");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("roles_claims");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("usuarios_tokens");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        ConfigurarEntidadesBase(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        CompletarCamposBase();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        CompletarCamposBase();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // Para toda entidad que hereda de EntidadBase: columnas comunes, índice por PF
    // y el filtro global (sólo datos del PF del token y sin los borrados).
    private void ConfigurarEntidadesBase(ModelBuilder modelBuilder)
    {
        var tipos = modelBuilder.Model.GetEntityTypes()
            .Where(tipo => typeof(EntidadBase).IsAssignableFrom(tipo.ClrType) && tipo.BaseType is null)
            .Select(tipo => tipo.ClrType)
            .ToList();

        var crearFiltro = typeof(AppDbContext).GetMethod(nameof(FiltroPorPf), BindingFlags.NonPublic | BindingFlags.Instance)!;

        foreach (var tipo in tipos)
        {
            var entidad = modelBuilder.Entity(tipo);
            entidad.HasKey(nameof(EntidadBase.Id));
            entidad.Property(nameof(EntidadBase.Id)).HasColumnName("id").ValueGeneratedNever();
            entidad.Property(nameof(EntidadBase.PfId)).HasColumnName("pf_id").IsRequired();
            entidad.Property(nameof(EntidadBase.CreadoEn)).HasColumnName("creado_en").IsRequired();
            entidad.Property(nameof(EntidadBase.BorradoEn)).HasColumnName("borrado_en");
            entidad.HasIndex(nameof(EntidadBase.PfId));
            entidad.HasQueryFilter((LambdaExpression)crearFiltro.MakeGenericMethod(tipo).Invoke(this, null)!);
        }
    }

    private Expression<Func<T, bool>> FiltroPorPf<T>() where T : EntidadBase =>
        entidad => entidad.PfId == PfIdActual && entidad.BorradoEn == null;

    // Al crear: PfId del token y CreadoEn. Al borrar: borrado lógico (BorradoEn).
    // PfId y CreadoEn no se pueden cambiar después.
    private void CompletarCamposBase()
    {
        var ahora = DateTime.UtcNow;

        foreach (var entrada in ChangeTracker.Entries<EntidadBase>())
        {
            switch (entrada.State)
            {
                case EntityState.Added:
                    entrada.Property(entidad => entidad.PfId).CurrentValue = usuarioActual.PfId
                        ?? throw new UnauthorizedAccessException("No se puede guardar sin un PF en el token.");
                    entrada.Property(entidad => entidad.CreadoEn).CurrentValue = ahora;
                    break;

                case EntityState.Modified:
                    entrada.Property(entidad => entidad.PfId).IsModified = false;
                    entrada.Property(entidad => entidad.CreadoEn).IsModified = false;
                    break;

                case EntityState.Deleted:
                    entrada.State = EntityState.Modified;
                    entrada.Property(entidad => entidad.BorradoEn).CurrentValue = ahora;
                    break;
            }
        }
    }
}
