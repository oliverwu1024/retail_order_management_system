using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Data.Configurations;

/// <summary>EF mapping for <see cref="ChatMessage"/> (DATABASE_DESIGN §3.21) — Phase 5A.</summary>
public sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("ChatMessage");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Role).HasColumnType("tinyint");
        builder.Property(m => m.Content).IsRequired();           // nvarchar(max)
        builder.Property(m => m.ToolName).HasMaxLength(80);
        // ToolPayloadJson is left unconstrained → nvarchar(max), nullable.

        builder.Property(m => m.CreatedBy).HasMaxLength(64);
        builder.Property(m => m.UpdatedBy).HasMaxLength(64);

        // Cascade: a message is a child of its session and cannot outlive it.
        builder.HasOne(m => m.ChatSession)
            .WithMany(s => s.Messages)
            .HasForeignKey(m => m.ChatSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Replay a conversation oldest-first (CreatedAt from IAuditableEntity).
        builder.HasIndex(m => new { m.ChatSessionId, m.CreatedAt }, "IX_ChatMessage_ChatSessionId_CreatedAt");
    }
}
