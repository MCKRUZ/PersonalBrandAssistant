using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class SocialInboxItemConfiguration : IEntityTypeConfiguration<SocialInboxItem>
{
    public void Configure(EntityTypeBuilder<SocialInboxItem> builder)
    {
        builder.ToTable("SocialInboxItems");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Platform).IsRequired();
        builder.Property(i => i.ItemType).IsRequired();
        builder.Property(i => i.AuthorName).IsRequired().HasMaxLength(200);
        builder.Property(i => i.AuthorProfileUrl).HasMaxLength(2000);
        builder.Property(i => i.Content).IsRequired().HasColumnType("text");
        builder.Property(i => i.SourceUrl).HasMaxLength(2000);
        builder.Property(i => i.PlatformItemId).IsRequired().HasMaxLength(200);
        builder.Property(i => i.DraftReply).HasColumnType("text");
        builder.Property(i => i.ReceivedAt).IsRequired();

        builder.HasIndex(i => new { i.Platform, i.PlatformItemId }).IsUnique();
        builder.HasIndex(i => new { i.IsRead, i.ReceivedAt });

        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Ignore(i => i.DomainEvents);
    }
}
