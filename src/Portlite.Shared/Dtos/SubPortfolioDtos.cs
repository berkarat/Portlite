namespace Portlite.Shared.Dtos;

public record SubPortfolioDto(
    Guid Id,
    string Name,
    string Code,
    string? Description,
    int DisplayOrder,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record CreateSubPortfolioRequest(
    string Name,
    string Code,
    string? Description,
    int DisplayOrder);

public record UpdateSubPortfolioRequest(
    string Name,
    string? Description,
    int DisplayOrder,
    bool IsActive);
