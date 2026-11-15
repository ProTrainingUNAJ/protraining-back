using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class OrganizationPhotoConfiguration : IEntityTypeConfiguration<OrganizationPhoto>
{
    public void Configure(EntityTypeBuilder<OrganizationPhoto> builder)
    {
        builder.ToTable("organization_photos");

        builder.Property(p => p.OrganizationId)
            .HasColumnName("organization_id")
            .IsRequired();

        builder.Property(p => p.Data)
            .HasColumnName("data")
            .IsRequired();

        builder.Property(p => p.ContentType)
            .HasColumnName("content_type")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.FileSize)
            .HasColumnName("file_size")
            .IsRequired();

        builder.Property(p => p.Width)
            .HasColumnName("width");

        builder.Property(p => p.Height)
            .HasColumnName("height");

        builder.HasIndex(p => p.OrganizationId)
            .IsUnique();
    }
}

