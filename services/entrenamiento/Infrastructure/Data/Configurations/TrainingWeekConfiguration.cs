using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public class TrainingWeekConfiguration : IEntityTypeConfiguration<TrainingWeek>
{
    public void Configure(EntityTypeBuilder<TrainingWeek> builder)
    {
        builder.ToTable("training_weeks");

        builder.Property(tw => tw.TrainingPlanId)
            .HasColumnName("training_plan_id")
            .IsRequired();

        builder.Property(tw => tw.WeekNumber)
            .HasColumnName("week_number")
            .IsRequired();

        
        builder.HasIndex(tw => new { tw.TrainingPlanId, tw.WeekNumber }).IsUnique();

        builder.HasMany(tw => tw.Sessions)
            .WithOne(ts => ts.Week)
            .HasForeignKey(ts => ts.TrainingWeekId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}