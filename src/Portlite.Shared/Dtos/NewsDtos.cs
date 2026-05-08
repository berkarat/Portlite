namespace Portlite.Shared.Dtos;

public record NewsItemDto(
    long Id,
    string Symbol,
    string Headline,
    string Summary,
    string Source,
    string Url,
    string? ImageUrl,
    DateTime PublishedAt,
    string Category);
