using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class AthletePhotoConfiguration : IEntityTypeConfiguration<AthletePhoto>
{
    public void Configure(EntityTypeBuilder<AthletePhoto> builder)
    {
        builder.ToTable("athlete_photos");

        builder.Property(p => p.AthleteId)
            .HasColumnName("athlete_id")
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

        builder.HasIndex(p => p.AthleteId)
            .IsUnique();
    }
}

