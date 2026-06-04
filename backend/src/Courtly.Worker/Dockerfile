# syntax=docker/dockerfile:1
# Multi-stage build for Courtly.Worker. Build context is the backend/ solution root (see docker-compose.yml).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore against just the project files first so this layer caches until a .csproj changes.
COPY Directory.Build.props ./
COPY src/Courtly.Domain/Courtly.Domain.csproj src/Courtly.Domain/
COPY src/Courtly.Contracts/Courtly.Contracts.csproj src/Courtly.Contracts/
COPY src/Courtly.Infrastructure/Courtly.Infrastructure.csproj src/Courtly.Infrastructure/
COPY src/Courtly.Worker/Courtly.Worker.csproj src/Courtly.Worker/
RUN dotnet restore src/Courtly.Worker/Courtly.Worker.csproj

# Copy the rest of the sources and publish.
COPY . .
RUN dotnet publish src/Courtly.Worker/Courtly.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app ./
# The healthcheck (docker-compose.yml) reads the liveness file refreshed by RabbitMqConnectionService.
ENTRYPOINT ["dotnet", "Courtly.Worker.dll"]
