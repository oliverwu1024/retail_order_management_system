using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Data.Configurations;

/// <summary>EF mapping for <see cref="ProcessedStripeEvent"/> (DATABASE_DESIGN §3.22).</summary>
public sealed class ProcessedStripeEventConfiguration : IEntityTypeConfiguration<ProcessedStripeEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedStripeEvent> builder)
    {
        builder.ToTable("ProcessedStripeEvent");
        builder.HasKey(e => e.Id);

        // bigint IDENTITY — a narrow, monotonic clustered key for an append-only log.
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.StripeEventId).IsRequired().HasMaxLength(80);
        builder.Property(e => e.EventType).IsRequired().HasMaxLength(80);

        // The UNIQUE index IS the idempotency guard: a redelivered event can't insert twice,
        // so the handler treats the duplicate-key violation as "already processed".
        builder.HasIndex(e => e.StripeEventId, "UX_ProcessedStripeEvent_StripeEventId").IsUnique();
    }
}
