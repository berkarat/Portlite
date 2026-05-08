using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portlite.Domain.Entities;

namespace Portlite.Infrastructure.Persistence.Configurations;

public class WatchlistItemConfiguration : IEntityTypeConfiguration<WatchlistItem>
{
    public void Configure(EntityTypeBuilder<WatchlistItem> builder)
    {
        builder.ToTable("WatchlistItems");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AssetSymbol).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(500);

        builder.HasOne(x => x.Asset)
            .WithMany()
            .HasForeignKey(x => x.AssetSymbol)
            .HasPrincipalKey(a => a.Symbol)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.AssetSymbol).IsUnique();
    }
}
