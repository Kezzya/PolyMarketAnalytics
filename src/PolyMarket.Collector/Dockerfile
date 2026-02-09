FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/PolyMarket.Contracts/PolyMarket.Contracts.csproj src/PolyMarket.Contracts/
COPY src/PolyMarket.Collector/PolyMarket.Collector.csproj src/PolyMarket.Collector/
RUN dotnet restore src/PolyMarket.Collector/PolyMarket.Collector.csproj

COPY src/PolyMarket.Contracts/ src/PolyMarket.Contracts/
COPY src/PolyMarket.Collector/ src/PolyMarket.Collector/
RUN dotnet publish src/PolyMarket.Collector/PolyMarket.Collector.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "PolyMarket.Collector.dll"]
