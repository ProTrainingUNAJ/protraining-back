using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class UserPhotoConfiguration : IEntityTypeConfiguration<UserPhoto>
{
    public void Configure(EntityTypeBuilder<UserPhoto> builder)
    {
        builder.ToTable("user_photos", "db_identity");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id");

        builder.Property(p => p.UserId)
            .HasColumnName("user_id")
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

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at");

        builder.HasIndex(p => p.UserId)
            .IsUnique();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

