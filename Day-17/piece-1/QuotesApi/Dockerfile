# Day-17 — multi-stage build for the Week-1 QuotesApi (.NET 10).
# Built remotely by `az acr build` (no local Docker needed) and run on
# Azure Container Apps. SQLite + connection string are supplied at runtime
# via Container App env vars, so nothing environment-specific is baked in.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY QuotesApi.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# Container Apps routes ingress to this port.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "QuotesApi.dll"]
