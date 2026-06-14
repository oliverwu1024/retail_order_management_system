using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Data.Configurations;

/// <summary>
/// EF Core mapping for <see cref="RefreshToken"/>. Picked up automatically by
/// <c>RetailDbContext.OnModelCreating</c>'s <c>ApplyConfigurationsFromAssembly</c>.
/// </summary>
public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");

        builder.HasKey(t => t.Id);

        // Identity's user id is nvarchar(450); the FK column matches.
        builder.Property(t => t.UserId)
            .IsRequired()
            .HasMaxLength(450);

        // SHA-256 hex is 64 chars; 128 leaves headroom.
        builder.Property(t => t.TokenHash)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(t => t.ReplacedByTokenHash).HasMaxLength(128);
        builder.Property(t => t.ReasonRevoked).HasMaxLength(64);

        // Audit columns (string user ids).
        builder.Property(t => t.CreatedBy).HasMaxLength(450);
        builder.Property(t => t.UpdatedBy).HasMaxLength(450);

        // Unique: the hash is the lookup key and must identify at most one row.
        builder.HasIndex(t => t.TokenHash).IsUnique();
        // Non-unique: "all tokens for this user" is the reuse-revocation query.
        builder.HasIndex(t => t.UserId);

        // Delete a user → delete their refresh tokens. The FK is configured
        // explicitly (rather than by convention) so the cascade is intentional.
        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
