using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public class TrainingPlanConfiguration : IEntityTypeConfiguration<TrainingPlan>
{
    public void Configure(EntityTypeBuilder<TrainingPlan> builder)
    {
        builder.ToTable("training_plans");

        builder.Property(tp => tp.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(tp => tp.Objective)
            .HasColumnName("objective")
            .HasMaxLength(500);

        builder.Property(tp => tp.IsTemplate)
            .HasColumnName("is_template")
            .HasDefaultValue(false)
            .IsRequired();

        builder.HasMany(tp => tp.Weeks)
            .WithOne(tw => tw.TrainingPlan)
            .HasForeignKey(tw => tw.TrainingPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(tp => tp.Assignments)
            .WithOne(pa => pa.Plan)
            .HasForeignKey(pa => pa.TrainingPlanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
