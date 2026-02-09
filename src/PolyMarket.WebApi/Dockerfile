FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/PolyMarket.Contracts/PolyMarket.Contracts.csproj src/PolyMarket.Contracts/
COPY src/PolyMarket.Storage/PolyMarket.Storage.csproj src/PolyMarket.Storage/
COPY src/PolyMarket.WebApi/PolyMarket.WebApi.csproj src/PolyMarket.WebApi/
RUN dotnet restore src/PolyMarket.WebApi/PolyMarket.WebApi.csproj

COPY src/PolyMarket.Contracts/ src/PolyMarket.Contracts/
COPY src/PolyMarket.Storage/ src/PolyMarket.Storage/
COPY src/PolyMarket.WebApi/ src/PolyMarket.WebApi/
RUN dotnet publish src/PolyMarket.WebApi/PolyMarket.WebApi.csproj -c Release -o /app --no-restore /p:ErrorOnDuplicatePublishOutputFiles=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "PolyMarket.WebApi.dll"]
