using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class AthleteVariantConfiguration : IEntityTypeConfiguration<AthleteVariant>
{
    public void Configure(EntityTypeBuilder<AthleteVariant> builder)
    {
        builder.ToTable("athlete_variants");

        builder.Property(v => v.AthleteId).HasColumnName("athlete_id").IsRequired();
        builder.Property(v => v.OriginalExerciseId).HasColumnName("original_exercise_id").IsRequired();
        builder.Property(v => v.SubstituteExerciseId).HasColumnName("substitute_exercise_id").IsRequired();
        builder.Property(v => v.IsApproved).HasColumnName("is_approved").IsRequired();
        builder.Property(v => v.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(v => v.ValidFrom).HasColumnName("valid_from").IsRequired();
        builder.Property(v => v.ValidTo).HasColumnName("valid_to");
        builder.Property(v => v.Note).HasColumnName("note").HasMaxLength(1000);

        builder.HasIndex(v => new { v.AthleteId, v.OriginalExerciseId, v.IsApproved, v.IsActive })
            .HasDatabaseName("IX_athlete_variants_athlete_original_status");
    }
}
