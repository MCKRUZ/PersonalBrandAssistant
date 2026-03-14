using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.UserId).IsRequired();
        builder.Property(n => n.Type).IsRequired();
        builder.Property(n => n.Title).IsRequired().HasMaxLength(500);
        builder.Property(n => n.Message).IsRequired().HasMaxLength(4000);
        builder.Property(n => n.CreatedAt).IsRequired();
        builder.Property(n => n.IsRead).HasDefaultValue(false);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Content>()
            .WithMany()
            .HasForeignKey(n => n.ContentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAt })
            .IsDescending(false, false, true);

        builder.Ignore(n => n.DomainEvents);
    }
}
