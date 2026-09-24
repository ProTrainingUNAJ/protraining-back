using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class AthleteConfiguration : IEntityTypeConfiguration<Athlete>
{
    public void Configure(EntityTypeBuilder<Athlete> builder)
    {
        builder.ToTable("athletes");
        builder.HasKey(athlete => athlete.Id);

        builder.Property(athlete => athlete.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(athlete => athlete.FullName)
            .HasColumnName("full_name")
            .HasMaxLength(150)
            .IsRequired()
            .IsConcurrencyToken();

        builder.Property(athlete => athlete.Category)
            .HasColumnName("category")
            .HasMaxLength(100)
            .IsRequired()
            .IsConcurrencyToken();

        builder.Property(athlete => athlete.Record1RmKg)
            .HasColumnName("record_1rm")
            .HasColumnType("numeric(5,2)")
            .IsRequired()
            .IsConcurrencyToken();

        builder.Property(athlete => athlete.Active)
            .HasColumnName("active")
            .HasDefaultValue(true)
            .IsRequired()
            .IsConcurrencyToken();
    }
}
