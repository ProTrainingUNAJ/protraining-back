using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public class TrainingBlockConfiguration : IEntityTypeConfiguration<TrainingBlock>
{
    public void Configure(EntityTypeBuilder<TrainingBlock> builder)
    {
        builder.ToTable("training_blocks");

        builder.Property(tb => tb.TrainingSessionId)
            .HasColumnName("training_session_id")
            .IsRequired();

        builder.Property(tb => tb.Title)
            .HasColumnName("title")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(tb => tb.Order)
            .HasColumnName("order_index")
            .IsRequired();

        builder.HasIndex(tb => new { tb.TrainingSessionId, tb.Order })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        builder.HasMany(tb => tb.PrescribedExercises)
            .WithOne(pe => pe.TrainingBlock)
            .HasForeignKey(pe => pe.TrainingBlockId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
