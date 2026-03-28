using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class EngagementTaskConfiguration : IEntityTypeConfiguration<EngagementTask>
{
    public void Configure(EntityTypeBuilder<EngagementTask> builder)
    {
        builder.ToTable("EngagementTasks");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Platform).IsRequired();
        builder.Property(e => e.TaskType).IsRequired();
        builder.Property(e => e.TargetCriteria).IsRequired().HasColumnType("jsonb");
        builder.Property(e => e.CronExpression).IsRequired().HasMaxLength(100);
        builder.Property(e => e.MaxActionsPerExecution).HasDefaultValue(3);
        builder.Property(e => e.SchedulingMode).HasDefaultValue(SchedulingMode.HumanLike);
        builder.Property(e => e.AutoRespond).HasDefaultValue(false);
        builder.Property(e => e.SkippedLastExecution).HasDefaultValue(false);

        builder.HasIndex(e => new { e.Platform, e.IsEnabled, e.AutoRespond, e.NextExecutionAt });

        builder.HasMany(e => e.Executions)
            .WithOne(ex => ex.EngagementTask)
            .HasForeignKey(ex => ex.EngagementTaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Ignore(e => e.DomainEvents);
    }
}
