using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class ContentCalendarSlotConfiguration : IEntityTypeConfiguration<ContentCalendarSlot>
{
    public void Configure(EntityTypeBuilder<ContentCalendarSlot> builder)
    {
        builder.ToTable("ContentCalendarSlots");

        builder.HasKey(s => s.Id);

        builder.HasIndex(s => new { s.ScheduledDate, s.TargetPlatform });

        builder.Property(s => s.TimeZoneId).IsRequired().HasMaxLength(100);
        builder.Property(s => s.Theme).HasMaxLength(200);
        builder.Property(s => s.RecurrencePattern).HasMaxLength(200);

        builder.HasOne<Content>()
            .WithMany()
            .HasForeignKey(s => s.ContentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Ignore(s => s.DomainEvents);
    }
}
