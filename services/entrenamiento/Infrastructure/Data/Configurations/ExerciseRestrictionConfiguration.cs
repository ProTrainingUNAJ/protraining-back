using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class ExerciseRestrictionConfiguration : IEntityTypeConfiguration<ExerciseRestriction>
{
    public void Configure(EntityTypeBuilder<ExerciseRestriction> builder)
    {
        builder.ToTable("exercise_restrictions");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(r => r.Zone)
            .HasColumnName("zone")
            .IsRequired();

        builder.Property(r => r.ExerciseId)
            .HasColumnName("exercise_id")
            .IsRequired();

        builder.Property(r => r.MinPainLevel)
            .HasColumnName("min_pain_level")
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(r => r.Reason)
            .HasColumnName("reason")
            .IsRequired();

        builder.HasOne(r => r.Exercise)
            .WithMany()
            .HasForeignKey(r => r.ExerciseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.Substitutes)
            .WithOne(s => s.Restriction)
            .HasForeignKey(s => s.RestrictionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.Zone);
        builder.HasIndex(r => new { r.Zone, r.ExerciseId }).IsUnique();
    }
}
