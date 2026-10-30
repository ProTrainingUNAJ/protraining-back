using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class WellnessReminderSentConfiguration : IEntityTypeConfiguration<WellnessReminderSent>
{
    public void Configure(EntityTypeBuilder<WellnessReminderSent> builder)
    {
        builder.ToTable("wellness_reminders_sent");

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(w => w.AthleteId)
            .HasColumnName("athlete_id")
            .IsRequired();

        builder.Property(w => w.Date)
            .HasColumnName("date")
            .IsRequired();

        builder.Property(w => w.SentAt)
            .HasColumnName("sent_at")
            .IsRequired();

        // indice unico para garantizar en base de datos un solo recordatorio por dia por atleta
        builder.HasIndex(w => new { w.AthleteId, w.Date })
            .IsUnique()
            .HasDatabaseName("ix_wellness_reminders_sent_athlete_id_date");
    }
}
