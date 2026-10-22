using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class AiLogConfiguration : IEntityTypeConfiguration<AiLog>
{
    public void Configure(EntityTypeBuilder<AiLog> builder)
    {
        builder.ToTable("ai_logs");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(l => l.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(l => l.PfId).HasColumnName("pf_id");
        builder.Property(l => l.AthleteId).HasColumnName("athlete_id");
        builder.Property(l => l.Provider).HasColumnName("provider").HasMaxLength(50).IsRequired();
        builder.Property(l => l.Prompt).HasColumnName("prompt").IsRequired();
        builder.Property(l => l.Response).HasColumnName("response");
        builder.Property(l => l.LatencyMs).HasColumnName("latency_ms").IsRequired();
        builder.Property(l => l.PromptTokens).HasColumnName("prompt_tokens");
        builder.Property(l => l.CompletionTokens).HasColumnName("completion_tokens");
        builder.Property(l => l.TotalTokens).HasColumnName("total_tokens");
        builder.Property(l => l.IsValid).HasColumnName("is_valid").IsRequired();
        builder.Property(l => l.ValidationResult).HasColumnName("validation_result").HasMaxLength(100).IsRequired();
        builder.Property(l => l.ErrorMessage).HasColumnName("error_message");

        builder.HasIndex(l => l.CreatedAt).HasDatabaseName("IX_ai_logs_created_at");
        builder.HasIndex(l => l.Provider).HasDatabaseName("IX_ai_logs_provider");
        builder.HasIndex(l => l.AthleteId).HasDatabaseName("IX_ai_logs_athlete_id");
        builder.HasIndex(l => l.PfId).HasDatabaseName("IX_ai_logs_pf_id");
    }
}
