using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class AttendanceConfiguration : IEntityTypeConfiguration<Attendance>
{
    public void Configure(EntityTypeBuilder<Attendance> builder)
    {
        builder.ToTable("attendances");
        builder.Property(attendance => attendance.AthleteId).HasColumnName("athlete_id").IsRequired();
        builder.Property(attendance => attendance.Date).HasColumnName("date").HasColumnType("date").IsRequired();
        builder.Property(attendance => attendance.IsPresent).HasColumnName("is_present").IsRequired();

        builder.HasIndex(attendance => new { attendance.AthleteId, attendance.Date })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");
    }
}