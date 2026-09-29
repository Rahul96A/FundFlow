# syntax=docker/dockerfile:1.7
# Build context is the repository root (central package management and global.json live there):
#   docker build -f src/FundFlow.Api/Dockerfile -t fundflow/api .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from project files only, so dependency layers are cached until a csproj or package version changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/FundFlow.Domain/FundFlow.Domain.csproj src/FundFlow.Domain/
COPY src/FundFlow.Contracts/FundFlow.Contracts.csproj src/FundFlow.Contracts/
COPY src/FundFlow.Application/FundFlow.Application.csproj src/FundFlow.Application/
COPY src/FundFlow.Infrastructure/FundFlow.Infrastructure.csproj src/FundFlow.Infrastructure/
COPY src/FundFlow.Api/FundFlow.Api.csproj src/FundFlow.Api/
RUN dotnet restore src/FundFlow.Api/FundFlow.Api.csproj

COPY src ./src
RUN dotnet publish src/FundFlow.Api/FundFlow.Api.csproj \
    --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
# curl is only here for the container health check.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_gcServer=0 \
    DOTNET_EnableDiagnostics=0

# Run as the unprivileged user that the base image provides.
USER $APP_UID
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
    CMD curl -fsS http://localhost:8080/health/live || exit 1

# `dotnet FundFlow.Api.dll --migrate` runs the explicit database migration step and exits.
ENTRYPOINT ["dotnet", "FundFlow.Api.dll"]
