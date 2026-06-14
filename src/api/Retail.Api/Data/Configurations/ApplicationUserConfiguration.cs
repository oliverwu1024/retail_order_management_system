using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Data.Configurations;

/// <summary>
/// EF Core mapping for the profile + audit columns added to
/// <see cref="ApplicationUser"/> on top of Identity's base <c>AspNetUsers</c>
/// schema. The base columns are configured by <c>IdentityDbContext</c>; this only
/// constrains our additions.
/// </summary>
public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.DisplayName).HasMaxLength(100);
        builder.Property(u => u.FirstName).HasMaxLength(100);
        builder.Property(u => u.LastName).HasMaxLength(100);

        // IAuditableEntity actor columns store Identity string ids (nvarchar(450)).
        builder.Property(u => u.CreatedBy).HasMaxLength(450);
        builder.Property(u => u.UpdatedBy).HasMaxLength(450);
    }
}
