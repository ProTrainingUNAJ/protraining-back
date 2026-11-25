using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organizations");

        builder.Property(organization => organization.Name)
            .HasColumnName("name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(organization => organization.Description)
            .HasColumnName("description")
            .HasMaxLength(500);

        builder.Property(organization => organization.HasPhoto)
            .HasColumnName("has_photo")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(organization => organization.PhotoUpdatedAt)
            .HasColumnName("photo_updated_at");

        // Unique per PF, ignoring soft-deleted rows.
        builder.HasIndex(organization => new { organization.PfId, organization.Name })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");
    }
}
