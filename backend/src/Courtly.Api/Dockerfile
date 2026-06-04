# syntax=docker/dockerfile:1
# Multi-stage build for Courtly.Api. Build context is the backend/ solution root (see docker-compose.yml).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore against just the project files first so this layer caches until a .csproj changes.
COPY Directory.Build.props ./
COPY src/Courtly.Domain/Courtly.Domain.csproj src/Courtly.Domain/
COPY src/Courtly.Contracts/Courtly.Contracts.csproj src/Courtly.Contracts/
COPY src/Courtly.Infrastructure/Courtly.Infrastructure.csproj src/Courtly.Infrastructure/
COPY src/Courtly.Application/Courtly.Application.csproj src/Courtly.Application/
COPY src/Courtly.Api/Courtly.Api.csproj src/Courtly.Api/
RUN dotnet restore src/Courtly.Api/Courtly.Api.csproj

# Copy the rest of the sources and publish.
COPY . .
RUN dotnet publish src/Courtly.Api/Courtly.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
# curl backs the container healthcheck against /health.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Courtly.Api.dll"]
