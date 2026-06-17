using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Data.Configurations;

/// <summary>EF mapping for <see cref="AuditLog"/> (DATABASE_DESIGN §3.16).</summary>
public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLog");
        builder.HasKey(a => a.Id);

        // bigint IDENTITY — a narrow, monotonic clustered key for an append-only log.
        builder.Property(a => a.Id).ValueGeneratedOnAdd();
        builder.Property(a => a.Actor).IsRequired().HasMaxLength(64);
        builder.Property(a => a.Action).IsRequired().HasMaxLength(40);
        builder.Property(a => a.EntityType).IsRequired().HasMaxLength(120);
        builder.Property(a => a.EntityId).IsRequired().HasMaxLength(64);
        builder.Property(a => a.BeforeJson).HasColumnType("nvarchar(max)");
        builder.Property(a => a.AfterJson).HasColumnType("nvarchar(max)");

        // The three search axes the viewer exposes (PHASE_3_SCOPE.md §7): recent-first by time,
        // by a specific entity, and by a specific actor over time.
        builder.HasIndex(a => a.OccurredAt, "IX_AuditLog_OccurredAt");
        builder.HasIndex(a => new { a.EntityType, a.EntityId }, "IX_AuditLog_EntityType_EntityId");
        builder.HasIndex(a => new { a.Actor, a.OccurredAt }, "IX_AuditLog_Actor_OccurredAt");
    }
}
