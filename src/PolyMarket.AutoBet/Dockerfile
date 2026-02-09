FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/PolyMarket.Contracts/PolyMarket.Contracts.csproj src/PolyMarket.Contracts/
COPY src/PolyMarket.AutoBet/PolyMarket.AutoBet.csproj src/PolyMarket.AutoBet/
RUN dotnet restore src/PolyMarket.AutoBet/PolyMarket.AutoBet.csproj

COPY src/PolyMarket.Contracts/ src/PolyMarket.Contracts/
COPY src/PolyMarket.AutoBet/ src/PolyMarket.AutoBet/
RUN dotnet publish src/PolyMarket.AutoBet/PolyMarket.AutoBet.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "PolyMarket.AutoBet.dll"]
