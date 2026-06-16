using Portlite.Domain.Common;
using Portlite.Domain.Entities;
using Portlite.Shared.Dtos;

namespace Portlite.Api.Mappings;

public static class DtoMappings
{
    public static SubPortfolioDto ToDto(this SubPortfolio sp) => new(
        sp.Id, sp.Name, sp.Code, sp.Description, sp.DisplayOrder, sp.IsActive,
        sp.CreatedAt, sp.UpdatedAt);

    public static AssetDto ToDto(this Asset a) => new(
        a.Id, a.Symbol, a.Name, a.Type, a.Currency,
        a.OptionDetail?.ToDto(), a.Theme);

    public static OptionDetailDto ToDto(this OptionDetail od) => new(
        od.UnderlyingSymbol, od.OptionType, od.Strike, od.Expiry, od.Multiplier);

    public static TradeDto ToDto(this Trade t) => new(
        t.Id, t.SubPortfolioId, t.AssetSymbol, t.Side, t.Quantity, t.Price, t.Fee,
        t.ExecutedAt, t.Notes);

    public static CashTransactionDto ToDto(this CashTransaction c) => new(
        c.Id, c.SubPortfolioId, c.Type, c.Amount.ToDto(), c.OccurredAt, c.Reference, c.Notes);

    public static MoneyDto ToDto(this Money m) => new(m.Amount, m.Currency);

    public static Money ToDomain(this MoneyDto m) => new(m.Amount, m.Currency);

    public static OptionDetail ToDomain(this OptionDetailDto od) => new()
    {
        UnderlyingSymbol = od.UnderlyingSymbol,
        OptionType = od.OptionType,
        Strike = od.Strike,
        Expiry = od.Expiry,
        Multiplier = od.Multiplier
    };
}
