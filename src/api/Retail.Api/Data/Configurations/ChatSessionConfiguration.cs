using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Data.Configurations;

/// <summary>EF mapping for <see cref="ChatSession"/> (DATABASE_DESIGN §3.20) — Phase 5A.</summary>
public sealed class ChatSessionConfiguration : IEntityTypeConfiguration<ChatSession>
{
    public void Configure(EntityTypeBuilder<ChatSession> builder)
    {
        builder.ToTable("ChatSession");
        builder.HasKey(s => s.Id);

        // GUID-as-string upsert key. char(36) (non-Unicode, fixed length) matches
        // DATABASE_DESIGN §3.20 — a GUID is ASCII, so char(36)=36 bytes vs nchar(36)=72;
        // .IsUnicode(false) is what makes EF emit char rather than nchar. Accommodates a
        // future Copilot Studio conversation id (Phase 6).
        builder.Property(s => s.ConversationId)
            .IsRequired()
            .HasMaxLength(36)
            .IsUnicode(false)
            .IsFixedLength();

        builder.Property(s => s.CreatedBy).HasMaxLength(64);
        builder.Property(s => s.UpdatedBy).HasMaxLength(64);

        // Restrict: a customer profile can't be hard-deleted while it owns chat sessions, and it
        // keeps a single cascade path (avoids SQL Server's multiple-cascade-paths error). The FK is
        // optional (nullable) to leave the anonymous/Copilot door open; 5A always sets it.
        builder.HasOne(s => s.CustomerProfile)
            .WithMany()
            .HasForeignKey(s => s.CustomerProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        // Exactly one session per client conversation id — the upsert key.
        builder.HasIndex(s => s.ConversationId, "UX_ChatSession_ConversationId")
            .IsUnique();

        // Admin diagnostics: a customer's sessions, most-recently-active first.
        builder.HasIndex(s => new { s.CustomerProfileId, s.LastMessageAt }, "IX_ChatSession_CustomerProfileId_LastMessageAt");
    }
}
