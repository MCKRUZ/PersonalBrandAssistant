using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class AgentExecutionLogConfiguration : IEntityTypeConfiguration<AgentExecutionLog>
{
    public void Configure(EntityTypeBuilder<AgentExecutionLog> builder)
    {
        builder.ToTable("AgentExecutionLogs");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.AgentExecutionId).IsRequired();
        builder.Property(l => l.StepNumber).IsRequired();
        builder.Property(l => l.StepType).IsRequired().HasMaxLength(50);
        builder.Property(l => l.TokensUsed).HasDefaultValue(0);
        builder.Property(l => l.Timestamp).IsRequired();

        builder.Property(l => l.Content).HasMaxLength(2000);

        builder.HasOne<AgentExecution>()
            .WithMany()
            .HasForeignKey(l => l.AgentExecutionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(l => l.AgentExecutionId);

        builder.Ignore(l => l.DomainEvents);
    }
}
