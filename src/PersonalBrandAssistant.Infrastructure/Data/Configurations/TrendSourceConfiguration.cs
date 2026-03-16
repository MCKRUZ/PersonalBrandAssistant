using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class TrendSourceConfiguration : IEntityTypeConfiguration<TrendSource>
{
    public void Configure(EntityTypeBuilder<TrendSource> builder)
    {
        builder.ToTable("TrendSources");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);
        builder.Property(s => s.ApiUrl).HasMaxLength(2000);
        builder.Property(s => s.PollIntervalMinutes).IsRequired();
        builder.Property(s => s.IsEnabled).IsRequired();

        builder.HasIndex(s => new { s.Name, s.Type }).IsUnique();

        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Ignore(s => s.DomainEvents);
    }
}
