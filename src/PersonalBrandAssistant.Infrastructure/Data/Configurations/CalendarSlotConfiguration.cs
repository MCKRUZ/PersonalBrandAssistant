using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class CalendarSlotConfiguration : IEntityTypeConfiguration<CalendarSlot>
{
    public void Configure(EntityTypeBuilder<CalendarSlot> builder)
    {
        builder.ToTable("CalendarSlots");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.ScheduledAt).IsRequired();
        builder.Property(s => s.Platform).IsRequired();
        builder.Property(s => s.Status).IsRequired().HasDefaultValue(CalendarSlotStatus.Open);

        builder.HasIndex(s => s.Status);
        builder.HasIndex(s => new { s.ScheduledAt, s.Platform });

        builder.HasOne<ContentSeries>()
            .WithMany()
            .HasForeignKey(s => s.ContentSeriesId)
            .OnDelete(DeleteBehavior.SetNull);

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
