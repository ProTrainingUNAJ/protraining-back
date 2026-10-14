using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public class TrainingPlanConfiguration : IEntityTypeConfiguration<TrainingPlan>
{
    public void Configure(EntityTypeBuilder<TrainingPlan> builder)
    {
        builder.ToTable("training_plans");

        builder.Property(x => x.Name)
            .HasColumnName("name")
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Objective)
            .HasColumnName("objective")
            .HasMaxLength(500);

        builder.Property(x => x.AthleteId)
            .HasColumnName("athlete_id")
            .IsRequired();

        builder.HasMany(x => x.Weeks)
            .WithOne(x => x.TrainingPlan)
            .HasForeignKey(x => x.TrainingPlanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}