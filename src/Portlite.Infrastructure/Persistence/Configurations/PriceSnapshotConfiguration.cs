using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portlite.Domain.Entities;

namespace Portlite.Infrastructure.Persistence.Configurations;

public class PriceSnapshotConfiguration : IEntityTypeConfiguration<PriceSnapshot>
{
    public void Configure(EntityTypeBuilder<PriceSnapshot> builder)
    {
        builder.ToTable("PriceSnapshots");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AssetSymbol).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Close).HasPrecision(18, 6);
        builder.Property(x => x.PreviousClose).HasPrecision(18, 6);
        builder.Property(x => x.Open).HasPrecision(18, 6);
        builder.Property(x => x.High).HasPrecision(18, 6);
        builder.Property(x => x.Low).HasPrecision(18, 6);
        builder.Property(x => x.Source).HasMaxLength(50).IsRequired();

        builder.HasOne(x => x.Asset)
            .WithMany(a => a.PriceSnapshots)
            .HasForeignKey(x => x.AssetSymbol)
            .HasPrincipalKey(a => a.Symbol)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.AssetSymbol, x.Date }).IsUnique();
    }
}
