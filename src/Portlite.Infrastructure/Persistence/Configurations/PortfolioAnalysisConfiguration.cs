using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portlite.Domain.Entities;

namespace Portlite.Infrastructure.Persistence.Configurations;

public class PortfolioAnalysisConfiguration : IEntityTypeConfiguration<PortfolioAnalysis>
{
    public void Configure(EntityTypeBuilder<PortfolioAnalysis> builder)
    {
        builder.ToTable("PortfolioAnalyses");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ContentHash).HasMaxLength(PortfolioAnalysis.ContentHashLength).IsRequired();
        builder.Property(x => x.ResultJson).IsRequired();

        builder.HasOne(x => x.SubPortfolio)
            .WithMany()
            .HasForeignKey(x => x.SubPortfolioId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.SubPortfolioId, x.GeneratedAt }).IsDescending(false, true);
        builder.HasIndex(x => new { x.SubPortfolioId, x.ContentHash });
    }
}
