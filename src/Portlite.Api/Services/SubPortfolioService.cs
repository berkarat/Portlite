using Microsoft.EntityFrameworkCore;
using Portlite.Api.Mappings;
using Portlite.Domain.Entities;
using Portlite.Infrastructure.Persistence;
using Portlite.Shared.Dtos;

namespace Portlite.Api.Services;

public class SubPortfolioService
{
    private readonly PortliteDbContext _db;

    public SubPortfolioService(PortliteDbContext db) => _db = db;

    public async Task<List<SubPortfolioDto>> ListAsync(CancellationToken ct = default)
    {
        var items = await _db.SubPortfolios
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Code)
            .ToListAsync(ct);
        return items.Select(x => x.ToDto()).ToList();
    }

    public async Task<SubPortfolioDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var sp = await _db.SubPortfolios.FindAsync([id], ct)
            ?? throw new NotFoundException($"SubPortfolio {id} not found.");
        return sp.ToDto();
    }

    public async Task<SubPortfolioDto> CreateAsync(CreateSubPortfolioRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) throw new ValidationException("Name is required.");
        if (string.IsNullOrWhiteSpace(req.Code)) throw new ValidationException("Code is required.");

        var codeExists = await _db.SubPortfolios.AnyAsync(x => x.Code == req.Code, ct);
        if (codeExists) throw new ConflictException($"Code '{req.Code}' is already in use.");

        var sp = new SubPortfolio
        {
            Name = req.Name.Trim(),
            Code = req.Code.Trim(),
            Description = req.Description,
            DisplayOrder = req.DisplayOrder,
            IsActive = true
        };
        _db.SubPortfolios.Add(sp);
        await _db.SaveChangesAsync(ct);
        return sp.ToDto();
    }

    public async Task<SubPortfolioDto> UpdateAsync(Guid id, UpdateSubPortfolioRequest req, CancellationToken ct = default)
    {
        var sp = await _db.SubPortfolios.FindAsync([id], ct)
            ?? throw new NotFoundException($"SubPortfolio {id} not found.");

        if (string.IsNullOrWhiteSpace(req.Name)) throw new ValidationException("Name is required.");

        sp.Name = req.Name.Trim();
        sp.Description = req.Description;
        sp.DisplayOrder = req.DisplayOrder;
        sp.IsActive = req.IsActive;

        await _db.SaveChangesAsync(ct);
        return sp.ToDto();
    }

    public async Task DeleteAsync(Guid id, bool force = false, CancellationToken ct = default)
    {
        var sp = await _db.SubPortfolios
            .Include(x => x.Trades)
            .Include(x => x.CashTransactions)
            .Include(x => x.ValueSnapshots)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException($"SubPortfolio {id} not found.");

        var hasChildren = sp.Trades.Any() || sp.CashTransactions.Any() || sp.ValueSnapshots.Any();
        if (hasChildren && !force)
        {
            throw new ConflictException(
                $"Bu portföyde {sp.Trades.Count} işlem, {sp.CashTransactions.Count} nakit hareketi ve " +
                $"{sp.ValueSnapshots.Count} snapshot var. Cascade silmek için force=true gönder.");
        }

        if (hasChildren)
        {
            _db.Trades.RemoveRange(sp.Trades);
            _db.CashTransactions.RemoveRange(sp.CashTransactions);
            _db.PortfolioValueSnapshots.RemoveRange(sp.ValueSnapshots);
        }
        _db.SubPortfolios.Remove(sp);
        await _db.SaveChangesAsync(ct);
    }
}
