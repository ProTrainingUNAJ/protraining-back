using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> builder)
    {
        builder.ToTable("push_subscriptions");

        builder.Property(p => p.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(p => p.AthleteId)
            .HasColumnName("athlete_id");

        builder.Property(p => p.Endpoint)
            .HasColumnName("endpoint")
            .HasMaxLength(1024)
            .IsRequired();

        builder.Property(p => p.P256dh)
            .HasColumnName("p256dh")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(p => p.Auth)
            .HasColumnName("auth")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(p => p.UserAgent)
            .HasColumnName("user_agent")
            .HasMaxLength(512);

        builder.Property(p => p.LastDeliveredAt)
            .HasColumnName("last_delivered_at");

        builder.Property(p => p.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.HasIndex(p => p.UserId);
        builder.HasIndex(p => p.AthleteId);
        builder.HasIndex(p => p.Endpoint)
            .IsUnique()
            .HasFilter("deleted_at IS NULL");
    }
}
