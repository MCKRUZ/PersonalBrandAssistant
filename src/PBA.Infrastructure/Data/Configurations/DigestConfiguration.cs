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
        // One digest per (date, kind): the Main and Microsoft briefs share a date.
        builder.HasIndex(d => new { d.Date, d.Kind }).IsUnique();

        builder.HasMany(d => d.Items)
            .WithOne(i => i.Digest!)
            .HasForeignKey(i => i.DigestId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
