using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class CheckInConfiguration : IEntityTypeConfiguration<CheckIn>
{
    public void Configure(EntityTypeBuilder<CheckIn> builder)
    {
        builder.ToTable("check_ins");
        builder.Property(c => c.AthleteId).HasColumnName("athlete_id").IsRequired();
        builder.Property(c => c.Date).HasColumnName("date").HasColumnType("date").IsRequired();
        builder.Property(c => c.TrafficLight).HasColumnName("traffic_light").IsRequired();
        builder.Property(c => c.TrafficLightReason).HasColumnName("traffic_light_reason");
        builder.Property(c => c.SleepHours).HasColumnName("sleep_hours").IsRequired();
        builder.Property(c => c.SleepQuality).HasColumnName("sleep_quality").IsRequired();
        builder.Property(c => c.MuscleSoreness).HasColumnName("muscle_soreness").IsRequired();
        builder.Property(c => c.FatigueLevel).HasColumnName("fatigue_level").IsRequired();
        builder.Property(c => c.MoodRate).HasColumnName("mood_rate").IsRequired();
        builder.Property(c => c.StressLevel).HasColumnName("stress_level").IsRequired();
        builder.Property(c => c.IsSick).HasColumnName("is_sick").IsRequired();
        builder.Property(c => c.EffortRate).HasColumnName("effort_rate");
        builder.Property(c => c.PainZones).HasColumnName("pain_zones");

        builder.HasIndex(c => new { c.AthleteId, c.Date })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");
    }
}