using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Portlite.Api.Services;
using Portlite.Domain.Entities;
using Portlite.Domain.Enums;
using Portlite.Infrastructure.Ai;
using Portlite.Infrastructure.MarketData;
using Portlite.Infrastructure.Persistence;
using Xunit;

namespace Portlite.Api.Tests.Services;

public class PortfolioAnalysisServiceTests
{
    private sealed class TestDb : IAsyncDisposable
    {
        public SqliteConnection Connection { get; }
        public PortliteDbContext Context { get; }

        public TestDb(SqliteConnection connection, PortliteDbContext context)
        {
            Connection = connection;
            Context = context;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private static async Task<TestDb> NewDbAsync()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();
        var opts = new DbContextOptionsBuilder<PortliteDbContext>()
            .UseSqlite(conn)
            .Options;
        var ctx = new PortliteDbContext(opts);
        await ctx.Database.EnsureCreatedAsync();
        return new TestDb(conn, ctx);
    }

    private static PortfolioAnalysisService NewService(
        PortliteDbContext db,
        Mock<IAiAnalysisClient>? aiMock = null)
    {
        aiMock ??= new Mock<IAiAnalysisClient>();
        var priceStore = new PriceSnapshotStore(db);
        var posCalc = new PositionCalculator(db, priceStore);
        var snap = new PortfolioSnapshotService(
            db, posCalc,
            Mock.Of<IPriceProvider>(),
            Mock.Of<IHistoricalPriceProvider>(),
            priceStore,
            NullLogger<PortfolioSnapshotService>.Instance);
        return new PortfolioAnalysisService(
            db, posCalc, snap, Mock.Of<IHistoricalPriceProvider>(), aiMock.Object,
            NullLogger<PortfolioAnalysisService>.Instance);
    }

    [Fact]
    public async Task AnalyzeAsync_NoPositions_ThrowsBadRequest()
    {
        await using var test = await NewDbAsync();
        var db = test.Context;

        var portfolio = new SubPortfolio { Id = Guid.NewGuid(), Name = "T", Code = "T", DisplayOrder = 1, IsActive = true };
        db.SubPortfolios.Add(portfolio);
        await db.SaveChangesAsync();

        var svc = NewService(db);

        Func<Task> act = () => svc.AnalyzeAsync(portfolio.Id, false);
        await act.Should().ThrowAsync<BadRequestException>();
    }

    [Fact]
    public async Task AnalyzeAsync_CacheHit_DoesNotCallAi()
    {
        await using var test = await NewDbAsync();
        var db = test.Context;

        var pid = Guid.NewGuid();
        db.SubPortfolios.Add(new SubPortfolio { Id = pid, Name = "T", Code = "T", DisplayOrder = 1, IsActive = true });
        db.Assets.Add(new Asset { Symbol = "TEST", Name = "Test", Type = AssetType.Stock, Currency = CurrencyCode.USD });
        db.Trades.Add(new Trade
        {
            SubPortfolioId = pid,
            AssetSymbol = "TEST",
            Side = TradeSide.Buy,
            Quantity = 10,
            Price = 100,
            Fee = 0,
            ExecutedAt = DateTime.UtcNow.AddDays(-5)
        });
        db.PriceSnapshots.Add(new PriceSnapshot
        {
            AssetSymbol = "TEST",
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            Close = 110m,
            Source = "test"
        });
        await db.SaveChangesAsync();

        var aiMock = new Mock<IAiAnalysisClient>();
        aiMock.Setup(x => x.CompleteJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new AiCompletionResult(
                  """{"summary":"s","warnings":[],"suggestions":[],"marketContext":"m"}""",
                  100, 50));

        var svc = NewService(db, aiMock);
        var first = await svc.AnalyzeAsync(pid, false);
        var second = await svc.AnalyzeAsync(pid, false);

        aiMock.Verify(x => x.CompleteJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        first.FromCache.Should().BeFalse();
        second.FromCache.Should().BeTrue();
    }

    [Fact]
    public async Task AnalyzeAsync_ForceRefresh_CallsAiAgain()
    {
        await using var test = await NewDbAsync();
        var db = test.Context;

        var pid = Guid.NewGuid();
        db.SubPortfolios.Add(new SubPortfolio { Id = pid, Name = "T", Code = "T", DisplayOrder = 1, IsActive = true });
        db.Assets.Add(new Asset { Symbol = "TEST", Name = "Test", Type = AssetType.Stock, Currency = CurrencyCode.USD });
        db.Trades.Add(new Trade
        {
            SubPortfolioId = pid,
            AssetSymbol = "TEST",
            Side = TradeSide.Buy,
            Quantity = 10,
            Price = 100,
            Fee = 0,
            ExecutedAt = DateTime.UtcNow.AddDays(-5)
        });
        db.PriceSnapshots.Add(new PriceSnapshot
        {
            AssetSymbol = "TEST",
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            Close = 110m,
            Source = "test"
        });
        await db.SaveChangesAsync();

        var aiMock = new Mock<IAiAnalysisClient>();
        aiMock.Setup(x => x.CompleteJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new AiCompletionResult(
                  """{"summary":"s","warnings":[],"suggestions":[],"marketContext":"m"}""",
                  100, 50));

        var svc = NewService(db, aiMock);
        await svc.AnalyzeAsync(pid, false);
        await svc.AnalyzeAsync(pid, forceRefresh: true);

        aiMock.Verify(x => x.CompleteJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}
