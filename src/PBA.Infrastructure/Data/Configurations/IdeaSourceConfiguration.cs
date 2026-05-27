using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PBA.Domain.Entities;

namespace PBA.Infrastructure.Data.Configurations;

public class IdeaSourceConfiguration : IEntityTypeConfiguration<IdeaSource>
{
    public void Configure(EntityTypeBuilder<IdeaSource> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);
        builder.Property(s => s.FeedUrl).HasMaxLength(2000);
        builder.Property(s => s.ApiUrl).HasMaxLength(2000);
        builder.Property(s => s.Category).IsRequired().HasMaxLength(100);
        builder.Property(s => s.LastError).HasMaxLength(2000);
    }
}
