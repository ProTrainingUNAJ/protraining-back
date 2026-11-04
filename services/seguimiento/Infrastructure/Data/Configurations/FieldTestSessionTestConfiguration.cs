using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class FieldTestSessionTestConfiguration : IEntityTypeConfiguration<FieldTestSessionTest>
{
    public void Configure(EntityTypeBuilder<FieldTestSessionTest> builder)
    {
        builder.ToTable("field_test_session_tests");
        builder.Property(link => link.SessionId).HasColumnName("session_id").IsRequired();
        builder.Property(link => link.CatalogTestId).HasColumnName("catalog_test_id").IsRequired();
        builder.Property(link => link.Order).HasColumnName("order").IsRequired();

        builder.HasIndex(link => link.SessionId);
    }
}
