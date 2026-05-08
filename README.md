# Portlite

Personal portfolio dashboard — mirrors a Midas brokerage account, with live prices, daily snapshots, AI-powered portfolio analysis, and a news feed scoped to held symbols.

**Stack:** .NET 8 · Blazor WASM · EF Core 8 · Azure SQL · Finnhub · Azure AI Foundry

---

## Setup

All credentials are stored in [.NET user-secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) (outside the repo).

### 1. Database

Create an Azure SQL database (or use SQL Server / LocalDB). Then:

```bash
cd src/Portlite.Api
dotnet user-secrets set "ConnectionStrings:Default" "Server=tcp:<your-server>.database.windows.net,1433;Initial Catalog=portlite-dev;User ID=<admin>;Password=<pwd>;Encrypt=True;"
```

### 2. Finnhub (price + company news)

Get a free key at [finnhub.io](https://finnhub.io):

```bash
dotnet user-secrets set "Finnhub:ApiKey" "<your_key>"
```

### 3. Azure AI Foundry (portfolio analysis)

Deploy any chat model in Azure AI Foundry, then:

```bash
dotnet user-secrets set "AzureFoundry:Endpoint" "https://<resource>.services.ai.azure.com/models"
dotnet user-secrets set "AzureFoundry:ApiKey"   "<your_key>"
dotnet user-secrets set "AzureFoundry:Model"    "gpt-4o-mini"
```

### 4. Apply migrations

```bash
dotnet ef database update \
  --project   src/Portlite.Infrastructure \
  --startup-project src/Portlite.Api
```

### 5. Run (two terminals)

```bash
cd src/Portlite.Api && dotnet run --launch-profile http   # http://localhost:5101
cd src/Portlite.Web && dotnet run --launch-profile http   # http://localhost:5012
```

Open `http://localhost:5012`.

---

## Project layout

```
src/
  Portlite.Domain/          entities, value objects, enums
  Portlite.Shared/          DTOs (shared between API and Web)
  Portlite.Infrastructure/  EF Core, migrations, market data providers
  Portlite.Api/             controllers, services, hosted jobs
  Portlite.Web/             Blazor WASM SPA
tests/
  Portlite.Api.Tests/
```

---

## Notes

- Daily snapshots are written by a background hosted service in the API.
- News + price data are cached in-memory with TTL to stay under Finnhub free-tier limits (60 req/min).
- AI analysis responses are persisted per portfolio with a content hash to allow cache reuse.
