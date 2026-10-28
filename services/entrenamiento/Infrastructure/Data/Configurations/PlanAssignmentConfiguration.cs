using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public class PlanAssignmentConfiguration : IEntityTypeConfiguration<PlanAssignment>
{
    public void Configure(EntityTypeBuilder<PlanAssignment> builder)
    {
        builder.ToTable("plan_assignments");

        builder.Property(pa => pa.TrainingPlanId)
            .HasColumnName("training_plan_id")
            .IsRequired();

        builder.Property(pa => pa.CategoryId)
            .HasColumnName("category_id");

        builder.Property(pa => pa.AthleteId)
            .HasColumnName("athlete_id");

        builder.Property(pa => pa.StartDate)
            .HasColumnName("start_date")
            .IsRequired();

        builder.HasOne(pa => pa.Plan)
            .WithMany(tp => tp.Assignments)
            .HasForeignKey(pa => pa.TrainingPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(pa => pa.TrainingPlanId);
        builder.HasIndex(pa => pa.CategoryId);
        builder.HasIndex(pa => pa.AthleteId);
    }
}