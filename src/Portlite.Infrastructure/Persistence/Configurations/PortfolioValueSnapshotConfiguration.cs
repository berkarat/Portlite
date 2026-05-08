using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portlite.Domain.Common;
using Portlite.Domain.Entities;

namespace Portlite.Infrastructure.Persistence.Configurations;

public class PortfolioValueSnapshotConfiguration : IEntityTypeConfiguration<PortfolioValueSnapshot>
{
    public void Configure(EntityTypeBuilder<PortfolioValueSnapshot> builder)
    {
        builder.ToTable("PortfolioValueSnapshots");
        builder.HasKey(x => x.Id);

        ConfigureMoney(builder, x => x.MarketValue, "MarketValue");
        ConfigureMoney(builder, x => x.CostBasis, "CostBasis");
        ConfigureMoney(builder, x => x.RealizedPnL, "RealizedPnL");
        ConfigureMoney(builder, x => x.UnrealizedPnL, "UnrealizedPnL");

        builder.HasOne(x => x.SubPortfolio)
            .WithMany(sp => sp.ValueSnapshots)
            .HasForeignKey(x => x.SubPortfolioId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.SubPortfolioId, x.Date }).IsUnique();
    }

    private static void ConfigureMoney(
        EntityTypeBuilder<PortfolioValueSnapshot> builder,
        Expression<Func<PortfolioValueSnapshot, Money>> selector,
        string prefix)
    {
        builder.ComplexProperty(selector, m =>
        {
            m.Property(p => p.Amount).HasColumnName($"{prefix}_Amount").HasPrecision(18, 6);
            m.Property(p => p.Currency).HasColumnName($"{prefix}_Currency").HasConversion<int>();
        });
    }
}
