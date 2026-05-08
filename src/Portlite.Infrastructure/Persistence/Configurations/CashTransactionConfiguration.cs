using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portlite.Domain.Entities;

namespace Portlite.Infrastructure.Persistence.Configurations;

public class CashTransactionConfiguration : IEntityTypeConfiguration<CashTransaction>
{
    public void Configure(EntityTypeBuilder<CashTransaction> builder)
    {
        builder.ToTable("CashTransactions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Type).HasConversion<int>();
        builder.Property(x => x.Reference).HasMaxLength(200);
        builder.Property(x => x.Notes).HasMaxLength(1000);

        builder.ComplexProperty(x => x.Amount, m =>
        {
            m.Property(p => p.Amount).HasColumnName("Amount").HasPrecision(18, 6);
            m.Property(p => p.Currency).HasColumnName("Currency").HasConversion<int>();
        });

        builder.HasOne(x => x.SubPortfolio)
            .WithMany(sp => sp.CashTransactions)
            .HasForeignKey(x => x.SubPortfolioId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.SubPortfolioId, x.OccurredAt });
    }
}
