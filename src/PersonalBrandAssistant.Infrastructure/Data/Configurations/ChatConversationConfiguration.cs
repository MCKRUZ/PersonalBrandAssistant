using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Data.Configurations;

public class ChatConversationConfiguration : IEntityTypeConfiguration<ChatConversation>
{
    public void Configure(EntityTypeBuilder<ChatConversation> builder)
    {
        builder.ToTable("ChatConversations");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.ContentId).IsRequired();
        builder.HasIndex(c => c.ContentId).IsUnique();

        builder.Property(c => c.Messages)
            .HasConversion(new JsonValueConverter<List<ChatMessage>>())
            .HasColumnType("jsonb");

        builder.Property(c => c.ConversationSummary).HasMaxLength(4000);
        builder.Property(c => c.LastMessageAt).IsRequired();

        builder.HasOne<Content>()
            .WithMany()
            .HasForeignKey(c => c.ContentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(c => c.DomainEvents);
    }
}
