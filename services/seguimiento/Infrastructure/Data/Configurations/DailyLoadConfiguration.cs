using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class DailyLoadConfiguration : IEntityTypeConfiguration<DailyLoad>
{
    public void Configure(EntityTypeBuilder<DailyLoad> builder)
    {
        builder.ToTable("daily_loads");
        builder.Property(load => load.AthleteId).HasColumnName("athlete_id").IsRequired();
        builder.Property(load => load.Date).HasColumnName("date").HasColumnType("date").IsRequired();
        builder.Property(load => load.GymMinutes).HasColumnName("gym_minutes").IsRequired();
        builder.Property(load => load.GymRpe).HasColumnName("gym_rpe");
        builder.Property(load => load.SportTrainingMinutes).HasColumnName("sport_training_minutes").IsRequired();
        builder.Property(load => load.SportTrainingRpe).HasColumnName("sport_training_rpe");
        builder.Property(load => load.Match1Minutes).HasColumnName("match1_minutes").IsRequired();
        builder.Property(load => load.Match1Rpe).HasColumnName("match1_rpe");
        builder.Property(load => load.Match2Minutes).HasColumnName("match2_minutes").IsRequired();
        builder.Property(load => load.Match2Rpe).HasColumnName("match2_rpe");
        builder.Property(load => load.IndividualWorkMinutes).HasColumnName("individual_work_minutes").IsRequired();
        builder.Property(load => load.IndividualWorkRpe).HasColumnName("individual_work_rpe");
        builder.Ignore(load => load.TotalMinutes);

        builder.HasIndex(load => new { load.AthleteId, load.Date })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");
    }
}
