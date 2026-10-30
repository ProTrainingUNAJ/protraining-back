using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class CompetitiveEventConfiguration : IEntityTypeConfiguration<CompetitiveEvent>
{
    public void Configure(EntityTypeBuilder<CompetitiveEvent> builder)
    {
        builder.ToTable("competitive_events");

        builder.Property(e => e.CategoryId)
            .HasColumnName("category_id")
            .IsRequired();

        builder.Property(e => e.Date)
            .HasColumnName("date")
            .IsRequired();

        builder.Property(e => e.StartTime)
            .HasColumnName("start_time");

        builder.Property(e => e.Opponent)
            .HasColumnName("opponent")
            .HasMaxLength(CompetitiveEvent.MaxOpponentLength)
            .IsRequired();

        builder.Property(e => e.Venue)
            .HasColumnName("venue")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.Notes)
            .HasColumnName("notes")
            .HasMaxLength(CompetitiveEvent.MaxNotesLength);

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(e => e.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.CategoryId, e.Date });
    }
}
