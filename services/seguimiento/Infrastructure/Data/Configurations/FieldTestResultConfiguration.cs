using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class FieldTestResultConfiguration : IEntityTypeConfiguration<FieldTestResult>
{
    public void Configure(EntityTypeBuilder<FieldTestResult> builder)
    {
        builder.ToTable("field_test_results");
        builder.Property(result => result.SessionId).HasColumnName("session_id").IsRequired();
        builder.Property(result => result.CatalogTestId).HasColumnName("catalog_test_id").IsRequired();
        builder.Property(result => result.AthleteId).HasColumnName("athlete_id").IsRequired();
        builder.Property(result => result.Value).HasColumnName("value").HasPrecision(10, 3).IsRequired();
        builder.Property(result => result.Attempt).HasColumnName("attempt").IsRequired();
        builder.Property(result => result.RecordedAt).HasColumnName("recorded_at").IsRequired();

        // CA2 de KAN-424: la base rechaza cargar dos veces el mismo (sesión, test, atleta,
        // intento). El filtro por deleted_at sigue el patrón de attendances.
        builder.HasIndex(result => new { result.SessionId, result.CatalogTestId, result.AthleteId, result.Attempt })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");
    }
}
