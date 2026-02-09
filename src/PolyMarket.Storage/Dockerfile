FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/PolyMarket.Contracts/PolyMarket.Contracts.csproj src/PolyMarket.Contracts/
COPY src/PolyMarket.Storage/PolyMarket.Storage.csproj src/PolyMarket.Storage/
RUN dotnet restore src/PolyMarket.Storage/PolyMarket.Storage.csproj

COPY src/PolyMarket.Contracts/ src/PolyMarket.Contracts/
COPY src/PolyMarket.Storage/ src/PolyMarket.Storage/
RUN dotnet publish src/PolyMarket.Storage/PolyMarket.Storage.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "PolyMarket.Storage.dll"]
