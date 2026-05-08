using Portlite.Domain.Enums;

namespace Portlite.Shared.Dtos;

public record CashTransactionDto(
    Guid Id,
    Guid SubPortfolioId,
    CashTxType Type,
    MoneyDto Amount,
    DateTime OccurredAt,
    string? Reference,
    string? Notes);

public record CreateCashTransactionRequest(
    CashTxType Type,
    MoneyDto Amount,
    DateTime OccurredAt,
    string? Reference,
    string? Notes);
