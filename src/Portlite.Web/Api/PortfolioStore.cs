using Portlite.Shared.Dtos;

namespace Portlite.Web.Api;

public class PortfolioStore
{
    private readonly ApiClient _api;
    public List<SubPortfolioDto> Portfolios { get; private set; } = new();
    public Guid? SelectedId { get; private set; }
    public SubPortfolioDto? Selected =>
        SelectedId.HasValue ? Portfolios.FirstOrDefault(p => p.Id == SelectedId.Value) : null;

    public event Action? Changed;

    public PortfolioStore(ApiClient api) => _api = api;

    public async Task RefreshAsync()
    {
        Portfolios = await _api.ListPortfoliosAsync() ?? new();
        if (!SelectedId.HasValue || Portfolios.All(p => p.Id != SelectedId.Value))
            SelectedId = Portfolios.FirstOrDefault()?.Id;
        Changed?.Invoke();
    }

    public void Select(Guid id)
    {
        SelectedId = id;
        Changed?.Invoke();
    }
}
