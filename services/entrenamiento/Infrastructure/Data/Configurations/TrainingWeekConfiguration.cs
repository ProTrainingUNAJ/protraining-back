using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public class TrainingWeekConfiguration : IEntityTypeConfiguration<TrainingWeek>
{
    public void Configure(EntityTypeBuilder<TrainingWeek> builder)
    {
        builder.ToTable("training_weeks");

        builder.Property(x => x.WeekNumber)
            .HasColumnName("week_number")
            .IsRequired();

        builder.Property(x => x.TrainingPlanId)
            .HasColumnName("training_plan_id")
            .IsRequired();

        builder.HasMany(x => x.Sessions)
            .WithOne(x => x.TrainingWeek)
            .HasForeignKey(x => x.TrainingWeekId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}