using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portlite.Domain.Entities;

namespace Portlite.Infrastructure.Persistence.Configurations;

public class SubPortfolioConfiguration : IEntityTypeConfiguration<SubPortfolio>
{
    public void Configure(EntityTypeBuilder<SubPortfolio> builder)
    {
        builder.ToTable("SubPortfolios");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);

        builder.HasIndex(x => x.Code).IsUnique();
    }
}
