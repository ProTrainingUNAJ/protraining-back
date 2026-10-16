using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class PendingPurgeConfiguration : IEntityTypeConfiguration<PendingPurge>
{
    public void Configure(EntityTypeBuilder<PendingPurge> builder)
    {
        builder.ToTable("pending_purges");

        builder.Property(p => p.AthleteId)
            .HasColumnName("athlete_id")
            .IsRequired();

        builder.Property(p => p.EntityType)
            .HasColumnName("entity_type")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.Status)
            .HasColumnName("status")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(p => p.ProcessedAt)
            .HasColumnName("processed_at");

        builder.HasIndex(p => p.AthleteId);
        builder.HasIndex(p => p.Status);
    }
}
