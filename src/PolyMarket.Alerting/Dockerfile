FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/PolyMarket.Contracts/PolyMarket.Contracts.csproj src/PolyMarket.Contracts/
COPY src/PolyMarket.Alerting/PolyMarket.Alerting.csproj src/PolyMarket.Alerting/
RUN dotnet restore src/PolyMarket.Alerting/PolyMarket.Alerting.csproj

COPY src/PolyMarket.Contracts/ src/PolyMarket.Contracts/
COPY src/PolyMarket.Alerting/ src/PolyMarket.Alerting/
RUN dotnet publish src/PolyMarket.Alerting/PolyMarket.Alerting.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "PolyMarket.Alerting.dll"]
