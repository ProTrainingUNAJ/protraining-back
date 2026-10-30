using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Data.Configurations
{
    public class ExerciseConfiguration : IEntityTypeConfiguration<Exercise>
    {
        public void Configure(EntityTypeBuilder<Exercise> builder)
        {
            builder.ToTable("exercises");

            builder.Property(e => e.Name).HasColumnName("name").IsRequired();
            builder.Property(e => e.MuscleGroup).HasColumnName("muscle_group").IsRequired();
            builder.Property(e => e.Description).HasColumnName("description");
            builder.Property(e => e.AnimationUrl).HasColumnName("animation_url");
            builder.Property(e => e.VideoUrl).HasColumnName("video_url");
            builder.Property(e => e.Equipment).HasColumnName("equipment");
            builder.Property(e => e.Images).HasColumnName("images");
            builder.Property(e => e.PrimaryMuscles).HasColumnName("primary_muscles");
            builder.Property(e => e.SecondaryMuscles).HasColumnName("secondary_muscles");
            builder.Property(e => e.ParentExerciseId).HasColumnName("parent_exercise_id");
            builder.Property(e => e.VariantLabel).HasColumnName("variant_label").HasMaxLength(100);
            builder.Ignore(e => e.IsVariant);

            // Variantes (KAN-554): FK a la base; no se puede borrar una base que tenga variantes.
            builder.HasOne<Exercise>()
                .WithMany()
                .HasForeignKey(e => e.ParentExerciseId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.HasIndex(e => e.ParentExerciseId);

            builder.HasIndex(e => e.MuscleGroup);
            builder.HasIndex(e => e.Name).HasMethod("gin").HasOperators("gin_trgm_ops");

        }
    }
}
