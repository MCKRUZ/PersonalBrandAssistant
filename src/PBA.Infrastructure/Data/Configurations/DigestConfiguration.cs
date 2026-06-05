using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PBA.Domain.Entities;

namespace PBA.Infrastructure.Data.Configurations;

public class DigestConfiguration : IEntityTypeConfiguration<Digest>
{
    public void Configure(EntityTypeBuilder<Digest> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Title).IsRequired().HasMaxLength(300);
        builder.Property(d => d.Intro).HasColumnType("text").IsRequired();
        builder.HasIndex(d => d.Date).IsUnique();

        builder.HasMany(d => d.Items)
            .WithOne(i => i.Digest!)
            .HasForeignKey(i => i.DigestId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
