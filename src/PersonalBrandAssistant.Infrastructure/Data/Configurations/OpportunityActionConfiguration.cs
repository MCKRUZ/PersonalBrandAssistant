using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class OpportunityActionConfiguration : IEntityTypeConfiguration<OpportunityAction>
{
    public void Configure(EntityTypeBuilder<OpportunityAction> builder)
    {
        builder.ToTable("OpportunityActions");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.PostUrl).IsRequired().HasMaxLength(2048);
        builder.Property(e => e.Platform).IsRequired();
        builder.Property(e => e.Status).IsRequired();

        builder.HasIndex(e => new { e.Platform, e.PostUrl }).IsUnique();

        builder.Ignore(e => e.DomainEvents);
    }
}
