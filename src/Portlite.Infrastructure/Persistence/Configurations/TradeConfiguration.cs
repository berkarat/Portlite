using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portlite.Domain.Entities;

namespace Portlite.Infrastructure.Persistence.Configurations;

public class TradeConfiguration : IEntityTypeConfiguration<Trade>
{
    public void Configure(EntityTypeBuilder<Trade> builder)
    {
        builder.ToTable("Trades");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AssetSymbol).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Side).HasConversion<int>();
        builder.Property(x => x.Quantity).HasPrecision(18, 6);
        builder.Property(x => x.Price).HasPrecision(18, 6);
        builder.Property(x => x.Fee).HasPrecision(18, 6);
        builder.Property(x => x.Notes).HasMaxLength(1000);

        builder.HasOne(x => x.SubPortfolio)
            .WithMany(sp => sp.Trades)
            .HasForeignKey(x => x.SubPortfolioId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Asset)
            .WithMany(a => a.Trades)
            .HasForeignKey(x => x.AssetSymbol)
            .HasPrincipalKey(a => a.Symbol)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.SubPortfolioId, x.ExecutedAt });
    }
}
