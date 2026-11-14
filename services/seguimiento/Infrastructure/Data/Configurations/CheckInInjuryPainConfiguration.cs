using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class CheckInInjuryPainConfiguration : IEntityTypeConfiguration<CheckInInjuryPain>
{
    public void Configure(EntityTypeBuilder<CheckInInjuryPain> builder)
    {
        builder.ToTable("check_in_injury_pains");
        builder.Property(p => p.CheckInId).HasColumnName("check_in_id").IsRequired();
        builder.Property(p => p.InjuryId).HasColumnName("injury_id").IsRequired();
        builder.Property(p => p.Level).HasColumnName("level").IsRequired();

        builder.HasIndex(p => p.CheckInId);
        builder.HasIndex(p => new { p.CheckInId, p.InjuryId })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");
    }
}
