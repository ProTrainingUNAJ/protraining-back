using System.Linq.Expressions;
using System.Reflection;
using Application.Interfaces.Services;
using Domain.Common;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
    : IdentityDbContext<User, IdentityRole<Guid>, Guid>(options)
{
    // EF reads it again on every query, so it is always the PF of the current request.
    // Without a token it is Guid.Empty and the filters return nothing.
    private Guid CurrentPfId => currentUser.PfId ?? Guid.Empty;

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 1. obligatorio para Identity: carga sus claves primarias
        base.OnModelCreating(modelBuilder);

        // 2. esquema "db_identity"
        modelBuilder.HasDefaultSchema("db_identity");

        // 3. nombres de tablas y columnas de Identity en ingles y snake_case
        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("users");
            b.Property(u => u.FullName).HasColumnName("full_name").HasMaxLength(200);
            b.Property(u => u.IsActive).HasColumnName("is_active");
            b.Property(u => u.CreatedAt).HasColumnName("created_at");
            b.Property(u => u.UpdatedAt).HasColumnName("updated_at");
        });
        modelBuilder.Entity<IdentityRole<Guid>>().ToTable("roles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        ConfigureBaseEntities(modelBuilder);

        // 4. configuracion de la tabla de tokens de refresco
        modelBuilder.Entity<RefreshToken>(b =>
        {
            b.ToTable("refresh_tokens");
            b.HasKey(rt => rt.Id);
            b.Property(rt => rt.Id).HasColumnName("id");
            b.Property(rt => rt.UserId).HasColumnName("user_id").IsRequired();
            b.Property(rt => rt.Token).HasColumnName("token").HasMaxLength(200).IsRequired();
            b.Property(rt => rt.DeviceId).HasColumnName("device_id").HasMaxLength(100).IsRequired();
            b.Property(rt => rt.ExpiresAt).HasColumnName("expires_at").IsRequired();
            b.Property(rt => rt.CreatedAt).HasColumnName("created_at").IsRequired();
            b.Property(rt => rt.IsRevoked).HasColumnName("is_revoked").IsRequired();
            b.Property(rt => rt.RevokedAt).HasColumnName("revoked_at");
            b.Property(rt => rt.ReplacedByToken).HasColumnName("replaced_by_token").HasMaxLength(200);

            b.HasIndex(rt => rt.Token).IsUnique();
            b.HasIndex(rt => new { rt.UserId, rt.DeviceId });

            b.HasOne(rt => rt.User)
                .WithMany()
                .HasForeignKey(rt => rt.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

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
        entity => entity.PfId == CurrentPfId && entity.DeletedAt == null;

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
                        ?? throw new UnauthorizedAccessException("No se puede guardar sin un PF en el token.");
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
