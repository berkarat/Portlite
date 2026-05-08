using Microsoft.EntityFrameworkCore;
using Portlite.Api.Mappings;
using Portlite.Domain.Entities;
using Portlite.Infrastructure.Persistence;
using Portlite.Shared.Dtos;

namespace Portlite.Api.Services;

public class CashTransactionService
{
    private readonly PortliteDbContext _db;

    public CashTransactionService(PortliteDbContext db) => _db = db;

    public async Task<List<CashTransactionDto>> ListByPortfolioAsync(Guid subPortfolioId, CancellationToken ct = default)
    {
        var portfolioExists = await _db.SubPortfolios.AnyAsync(x => x.Id == subPortfolioId, ct);
        if (!portfolioExists) throw new NotFoundException($"SubPortfolio {subPortfolioId} not found.");

        var items = await _db.CashTransactions
            .Where(x => x.SubPortfolioId == subPortfolioId)
            .OrderByDescending(x => x.OccurredAt)
            .ToListAsync(ct);
        return items.Select(x => x.ToDto()).ToList();
    }

    public async Task<CashTransactionDto> CreateAsync(Guid subPortfolioId, CreateCashTransactionRequest req, CancellationToken ct = default)
    {
        var portfolioExists = await _db.SubPortfolios.AnyAsync(x => x.Id == subPortfolioId, ct);
        if (!portfolioExists) throw new NotFoundException($"SubPortfolio {subPortfolioId} not found.");

        if (req.Amount.Amount == 0) throw new ValidationException("Amount cannot be zero.");

        var tx = new CashTransaction
        {
            SubPortfolioId = subPortfolioId,
            Type = req.Type,
            Amount = req.Amount.ToDomain(),
            OccurredAt = req.OccurredAt,
            Reference = req.Reference,
            Notes = req.Notes
        };
        _db.CashTransactions.Add(tx);
        await _db.SaveChangesAsync(ct);
        return tx.ToDto();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var tx = await _db.CashTransactions.FindAsync([id], ct)
            ?? throw new NotFoundException($"Cash transaction {id} not found.");
        _db.CashTransactions.Remove(tx);
        await _db.SaveChangesAsync(ct);
    }
}
