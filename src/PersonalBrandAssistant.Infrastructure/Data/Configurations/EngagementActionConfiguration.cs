using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class EngagementActionConfiguration : IEntityTypeConfiguration<EngagementAction>
{
    public void Configure(EntityTypeBuilder<EngagementAction> builder)
    {
        builder.ToTable("EngagementActions");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.ActionType).IsRequired();
        builder.Property(a => a.TargetUrl).IsRequired().HasMaxLength(2000);
        builder.Property(a => a.GeneratedContent).HasColumnType("text");
        builder.Property(a => a.PlatformPostId).HasMaxLength(200);
        builder.Property(a => a.ErrorMessage).HasMaxLength(2000);
        builder.Property(a => a.PerformedAt).IsRequired();

        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Ignore(a => a.DomainEvents);
    }
}
