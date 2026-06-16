using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portlite.Domain.Entities;

namespace Portlite.Infrastructure.Persistence.Configurations;

public class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.ToTable("Assets");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Symbol).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Type).HasConversion<int>();
        builder.Property(x => x.Currency).HasConversion<int>();
        builder.Property(x => x.Theme).HasMaxLength(100);

        builder.HasIndex(x => x.Symbol).IsUnique();

        builder.OwnsOne(x => x.OptionDetail, od =>
        {
            od.Property(p => p.UnderlyingSymbol).HasMaxLength(50).HasColumnName("Option_UnderlyingSymbol");
            od.Property(p => p.OptionType).HasConversion<int>().HasColumnName("Option_Type");
            od.Property(p => p.Strike).HasPrecision(18, 6).HasColumnName("Option_Strike");
            od.Property(p => p.Expiry).HasColumnName("Option_Expiry");
            od.Property(p => p.Multiplier).HasColumnName("Option_Multiplier");
        });
    }
}
