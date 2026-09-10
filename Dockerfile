# Multi-stage build for Portlite.Api (serves the Blazor WASM UI from wwwroot).
# Builds entirely inside Docker so the VM host does not need the .NET SDK.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore Portlite.sln
RUN dotnet publish src/Portlite.Api/Portlite.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Portlite.Api.dll"]
