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

            builder.HasIndex(e => e.MuscleGroup);
        }
    }
}
