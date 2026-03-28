using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class WorkflowTransitionLogConfiguration : IEntityTypeConfiguration<WorkflowTransitionLog>
{
    public void Configure(EntityTypeBuilder<WorkflowTransitionLog> builder)
    {
        builder.ToTable("WorkflowTransitionLogs");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.ContentId).IsRequired();
        builder.Property(w => w.FromStatus).IsRequired();
        builder.Property(w => w.ToStatus).IsRequired();
        builder.Property(w => w.ActorType).IsRequired();
        builder.Property(w => w.Timestamp).IsRequired();
        builder.Property(w => w.Reason).HasMaxLength(2000);
        builder.Property(w => w.ActorId).HasMaxLength(500);

        builder.HasOne<Content>()
            .WithMany()
            .HasForeignKey(w => w.ContentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(w => new { w.ContentId, w.Timestamp })
            .IsDescending(false, true);

        builder.HasIndex(w => w.Timestamp);

        builder.Ignore(w => w.DomainEvents);
    }
}
