using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PBA.Domain.Entities;

namespace PBA.Infrastructure.Data.Configurations;

public class FeedItemConfiguration : IEntityTypeConfiguration<FeedItem>
{
    public void Configure(EntityTypeBuilder<FeedItem> builder)
    {
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Title).IsRequired().HasMaxLength(500);
        builder.Property(f => f.Summary).HasMaxLength(2000);
        builder.Property(f => f.Data).HasColumnType("jsonb");
        builder.Property(f => f.ActionType).HasMaxLength(100);

        builder.HasIndex(f => new { f.IsRead, f.CreatedAt });
        builder.HasIndex(f => new { f.Type, f.IsActedOn });
        builder.HasIndex(f => new { f.Type, f.CreatedAt });
    }
}
