using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class CategoryInvitationConfiguration : IEntityTypeConfiguration<CategoryInvitation>
{
    public void Configure(EntityTypeBuilder<CategoryInvitation> builder)
    {
        builder.ToTable("category_invitations");

        builder.Property(invitation => invitation.CategoryId)
            .HasColumnName("category_id")
            .IsRequired();

        builder.Property(invitation => invitation.Token)
            .HasColumnName("token")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(invitation => invitation.ExpiresAt)
            .HasColumnName("expires_at")
            .IsRequired();

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(invitation => invitation.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        // El token debe ser unico (ignorando registros con borrado logico)
        builder.HasIndex(invitation => invitation.Token)
            .IsUnique()
            .HasFilter("deleted_at IS NULL");
    }
}