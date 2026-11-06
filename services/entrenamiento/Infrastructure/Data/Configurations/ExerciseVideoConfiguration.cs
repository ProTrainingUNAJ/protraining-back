using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations
{
    public class ExerciseVideoConfiguration : IEntityTypeConfiguration<ExerciseVideo>
    {
        public void Configure(EntityTypeBuilder<ExerciseVideo> builder)
        {
            builder.ToTable("exercise_videos");

            builder.Property(e => e.PfId).HasColumnName("pf_id");
            builder.Property(e => e.ExerciseId).HasColumnName("exercise_id");
            builder.Property(e => e.VideoUrl).HasColumnName("video_url");

            builder.HasIndex(e => new { e.PfId, e.ExerciseId }).IsUnique().HasFilter("deleted_at IS NULL");

        }
    }
}
