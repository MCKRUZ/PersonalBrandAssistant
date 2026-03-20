using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class InterestKeywordConfiguration : IEntityTypeConfiguration<InterestKeyword>
{
    public void Configure(EntityTypeBuilder<InterestKeyword> builder)
    {
        builder.ToTable("InterestKeywords");

        builder.HasKey(k => k.Id);

        builder.Property(k => k.Keyword).IsRequired().HasMaxLength(200);
        builder.Property(k => k.Weight).IsRequired();
        builder.Property(k => k.MatchCount).IsRequired();

        builder.HasIndex(k => k.Keyword).IsUnique();

        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Ignore(k => k.DomainEvents);
    }
}
