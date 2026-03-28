using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class SavedTrendItemConfiguration : IEntityTypeConfiguration<SavedTrendItem>
{
    public void Configure(EntityTypeBuilder<SavedTrendItem> builder)
    {
        builder.ToTable("SavedTrendItems");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.TrendItemId).IsRequired();
        builder.Property(s => s.SavedAt).IsRequired();
        builder.Property(s => s.Notes).HasMaxLength(1000);

        builder.HasOne(s => s.TrendItem)
            .WithMany()
            .HasForeignKey(s => s.TrendItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => s.TrendItemId).IsUnique();

        builder.Ignore(s => s.DomainEvents);
    }
}
