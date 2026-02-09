FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/PolyMarket.Contracts/PolyMarket.Contracts.csproj src/PolyMarket.Contracts/
COPY src/PolyMarket.Analytics/PolyMarket.Analytics.csproj src/PolyMarket.Analytics/
RUN dotnet restore src/PolyMarket.Analytics/PolyMarket.Analytics.csproj

COPY src/PolyMarket.Contracts/ src/PolyMarket.Contracts/
COPY src/PolyMarket.Analytics/ src/PolyMarket.Analytics/
RUN dotnet publish src/PolyMarket.Analytics/PolyMarket.Analytics.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "PolyMarket.Analytics.dll"]
