# ─── Build stage ────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution-level files for restore
COPY Directory.Build.props ./
COPY Directory.Packages.props ./
COPY eShop.sln ./

# Copy project files (enough for restore)
COPY src/BuildingBlocks/EShop.Contracts/EShop.Contracts.csproj                 src/BuildingBlocks/EShop.Contracts/
COPY src/BuildingBlocks/EShop.Messaging.Kafka/EShop.Messaging.Kafka.csproj     src/BuildingBlocks/EShop.Messaging.Kafka/
COPY src/Services/Order/Order.Api/Order.Api.csproj                             src/Services/Order/Order.Api/

RUN dotnet restore src/Services/Order/Order.Api/Order.Api.csproj --no-build-tree

# Copy the rest of the source
COPY src/ src/

RUN dotnet publish src/Services/Order/Order.Api/Order.Api.csproj \
    -c Release \
    --no-restore \
    -o /app/out

# ─── Runtime stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN adduser --disabled-password --gecos '' appuser && chown -R appuser /app
USER appuser

COPY --from=build /app/out ./

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Order.Api.dll"]
