using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class BlogPublishRequestConfiguration : IEntityTypeConfiguration<BlogPublishRequest>
{
    public void Configure(EntityTypeBuilder<BlogPublishRequest> builder)
    {
        builder.ToTable("BlogPublishRequests");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.ContentId).IsRequired();
        builder.Property(b => b.Html).IsRequired();
        builder.Property(b => b.TargetPath).IsRequired().HasMaxLength(500);
        builder.Property(b => b.Status).IsRequired().HasDefaultValue(BlogPublishStatus.Staged);
        builder.Property(b => b.CommitSha).HasMaxLength(40);
        builder.Property(b => b.CommitUrl).HasMaxLength(2000);
        builder.Property(b => b.BlogUrl).HasMaxLength(2000);
        builder.Property(b => b.ErrorMessage).HasMaxLength(4000);
        builder.Property(b => b.VerificationAttempts).IsRequired().HasDefaultValue(0);

        builder.HasOne<Content>()
            .WithMany()
            .HasForeignKey(b => b.ContentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Ignore(b => b.DomainEvents);
    }
}
