using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Portlite.Infrastructure.MarketData;
using Portlite.Infrastructure.Persistence;

namespace Portlite.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPortliteInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Connection string 'Default' is missing. Set it via user-secrets or appsettings.");

        services.AddDbContext<PortliteDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(PortliteDbContext).Assembly.FullName);
                sql.EnableRetryOnFailure();
            }));

        services.AddScoped<PriceSnapshotStore>();

        services.Configure<FinnhubOptions>(configuration.GetSection(FinnhubOptions.SectionName));

        services.AddHttpClient<IPriceProvider, FinnhubPriceProvider>((sp, client) =>
        {
            var opts = configuration.GetSection(FinnhubOptions.SectionName).Get<FinnhubOptions>()
                ?? new FinnhubOptions();
            client.BaseAddress = new Uri(opts.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(10);
        }).AddStandardResilienceHandler();

        // INewsProvider shares the same FinnhubPriceProvider instance via DI scope.
        services.AddScoped<INewsProvider>(sp => (FinnhubPriceProvider)sp.GetRequiredService<IPriceProvider>());

        return services;
    }
}
