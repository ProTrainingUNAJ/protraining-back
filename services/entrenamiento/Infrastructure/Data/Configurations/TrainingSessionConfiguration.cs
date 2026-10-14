using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public class TrainingSessionConfiguration : IEntityTypeConfiguration<TrainingSession>
{
    public void Configure(EntityTypeBuilder<TrainingSession> builder)
    {
        builder.ToTable("training_sessions");

        builder.Property(x => x.Name)
            .HasColumnName("name")
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Order)
            .HasColumnName("order_index")
            .IsRequired();

        builder.Property(x => x.TrainingWeekId)
            .HasColumnName("training_week_id")
            .IsRequired();

        builder.HasMany(x => x.PrescribedExercises)
            .WithOne(x => x.TrainingSession)
            .HasForeignKey(x => x.TrainingSessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}