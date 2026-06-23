using Microsoft.EntityFrameworkCore;
using Portlite.Domain.Common;
using Portlite.Domain.Entities;

namespace Portlite.Infrastructure.Persistence;

public class PortliteDbContext : DbContext
{
    public PortliteDbContext(DbContextOptions<PortliteDbContext> options) : base(options) { }

    public DbSet<SubPortfolio> SubPortfolios => Set<SubPortfolio>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<CashTransaction> CashTransactions => Set<CashTransaction>();
    public DbSet<PriceSnapshot> PriceSnapshots => Set<PriceSnapshot>();
    public DbSet<PortfolioValueSnapshot> PortfolioValueSnapshots => Set<PortfolioValueSnapshot>();
    public DbSet<WatchlistItem> WatchlistItems => Set<WatchlistItem>();
    public DbSet<PortfolioAnalysis> PortfolioAnalyses => Set<PortfolioAnalysis>();
    public DbSet<PorttechReport> PorttechReports => Set<PorttechReport>();
    public DbSet<PositionCostOverride> PositionCostOverrides => Set<PositionCostOverride>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PortliteDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }

    public override int SaveChanges()
    {
        StampAuditFields();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAuditFields();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void StampAuditFields()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
