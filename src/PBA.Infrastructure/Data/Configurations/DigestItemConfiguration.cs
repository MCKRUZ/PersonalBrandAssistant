using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PBA.Domain.Entities;

namespace PBA.Infrastructure.Data.Configurations;

public class DigestItemConfiguration : IEntityTypeConfiguration<DigestItem>
{
    public void Configure(EntityTypeBuilder<DigestItem> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.WhyItMatters).HasColumnType("text").IsRequired();

        builder.HasOne(i => i.Idea)
            .WithMany()
            .HasForeignKey(i => i.IdeaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => i.DigestId);
    }
}
