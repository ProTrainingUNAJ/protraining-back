using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Infrastructure.Data.Configurations;

public sealed class AthleteRecordConfiguration : IEntityTypeConfiguration<AthleteRecord>
{
    public void Configure(EntityTypeBuilder<AthleteRecord> builder)
    {
        builder.ToTable("athlete_records");
        builder.Property(r => r.AthleteId)
            .HasColumnName("athlete_id")
            .IsRequired();
        builder.Property(r => r.ExerciseId)
            .HasColumnName("exercise_id")
            .IsRequired();
        builder.Property(r => r.Weight)
            .HasColumnName("weight")
            .HasPrecision(6, 2)
            .IsRequired();
        builder.Property(r => r.Date)
            .HasColumnName("date")
            .IsRequired();
        builder.Property(r => r.IsActive)
            .HasColumnName("is_active")
            .IsRequired();
        builder.Property(r => r.Source)
            .HasColumnName("source")
            .IsRequired();
    }
}