# ─── Build stage ────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY Directory.Packages.props ./
COPY eShop.sln ./

COPY src/BuildingBlocks/EShop.Contracts/EShop.Contracts.csproj                      src/BuildingBlocks/EShop.Contracts/
COPY src/BuildingBlocks/EShop.Messaging.Kafka/EShop.Messaging.Kafka.csproj          src/BuildingBlocks/EShop.Messaging.Kafka/
COPY src/Services/Notification/Notification.Service/Notification.Service.csproj     src/Services/Notification/Notification.Service/

RUN dotnet restore src/Services/Notification/Notification.Service/Notification.Service.csproj --no-build-tree

COPY src/ src/

RUN dotnet publish src/Services/Notification/Notification.Service/Notification.Service.csproj \
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

ENTRYPOINT ["dotnet", "Notification.Service.dll"]
