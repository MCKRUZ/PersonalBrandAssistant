using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class UserNotificationConfiguration : IEntityTypeConfiguration<UserNotification>
{
    public void Configure(EntityTypeBuilder<UserNotification> builder)
    {
        builder.ToTable("UserNotifications");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Type).IsRequired().HasMaxLength(200);
        builder.Property(n => n.Message).IsRequired().HasMaxLength(4000);
        builder.Property(n => n.Status).IsRequired().HasDefaultValue(NotificationStatus.Pending);
        builder.Property(n => n.CreatedAt).IsRequired();

        builder.HasIndex(n => new { n.ContentId, n.Type })
            .IsUnique()
            .HasFilter("\"Status\" = 0");

        builder.HasOne<Content>()
            .WithMany()
            .HasForeignKey(n => n.ContentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Ignore(n => n.DomainEvents);
    }
}
