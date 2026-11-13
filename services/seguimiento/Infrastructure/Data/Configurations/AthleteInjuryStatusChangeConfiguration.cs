using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class AthleteInjuryStatusChangeConfiguration : IEntityTypeConfiguration<AthleteInjuryStatusChange>
{
    public void Configure(EntityTypeBuilder<AthleteInjuryStatusChange> builder)
    {
        builder.ToTable("athlete_injury_status_changes");
        builder.Property(c => c.InjuryId).HasColumnName("injury_id").IsRequired();
        builder.Property(c => c.FromStatus).HasColumnName("from_status");
        builder.Property(c => c.ToStatus).HasColumnName("to_status").IsRequired();
        builder.Property(c => c.Note).HasColumnName("note").IsRequired();
        builder.Property(c => c.ChangedAt).HasColumnName("changed_at").IsRequired();
        builder.Property(c => c.ChangedBy).HasColumnName("changed_by").IsRequired();

        builder.HasIndex(c => c.InjuryId);
    }
}
