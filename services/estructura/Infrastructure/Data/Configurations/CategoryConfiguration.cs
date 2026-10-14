using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");

        builder.Property(category => category.OrganizationId)
            .HasColumnName("organization_id")
            .IsRequired();

        builder.Property(category => category.Name)
            .HasColumnName("name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(category => category.Description)
            .HasColumnName("description")
            .HasMaxLength(500);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(category => category.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique per organization, ignoring soft-deleted rows.
        builder.HasIndex(category => new { category.OrganizationId, category.Name })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");
    }
}
