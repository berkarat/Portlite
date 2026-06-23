using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portlite.Domain.Entities;

namespace Portlite.Infrastructure.Persistence.Configurations;

public class PositionCostOverrideConfiguration : IEntityTypeConfiguration<PositionCostOverride>
{
    public void Configure(EntityTypeBuilder<PositionCostOverride> builder)
    {
        builder.ToTable("PositionCostOverrides");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AssetSymbol).HasMaxLength(50).IsRequired();
        builder.Property(x => x.AverageCost).HasPrecision(18, 6);

        builder.HasIndex(x => new { x.SubPortfolioId, x.AssetSymbol }).IsUnique();

        builder.HasOne(x => x.SubPortfolio)
            .WithMany()
            .HasForeignKey(x => x.SubPortfolioId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Asset)
            .WithMany()
            .HasForeignKey(x => x.AssetSymbol)
            .HasPrincipalKey(a => a.Symbol)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
