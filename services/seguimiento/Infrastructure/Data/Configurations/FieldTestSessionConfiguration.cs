using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class FieldTestSessionConfiguration : IEntityTypeConfiguration<FieldTestSession>
{
    public void Configure(EntityTypeBuilder<FieldTestSession> builder)
    {
        builder.ToTable("field_test_sessions");
        builder.Property(session => session.CategoryId).HasColumnName("category_id").IsRequired();
        builder.Property(session => session.Date).HasColumnName("date").HasColumnType("date").IsRequired();
        builder.Property(session => session.Name).HasColumnName("name").HasMaxLength(150).IsRequired();

        // El estado viaja como nombre ("Planned", "InProgress", "Closed"): mismo contrato
        // con el front-end y la BD que CatalogTest.MotorCapacity.
        builder.Property(session => session.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(session => session.CategoryId);
    }
}
