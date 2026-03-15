FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/HouseFlow.Core/HouseFlow.Core.csproj HouseFlow.Core/
COPY src/HouseFlow.Application/HouseFlow.Application.csproj HouseFlow.Application/
COPY src/HouseFlow.Infrastructure/HouseFlow.Infrastructure.csproj HouseFlow.Infrastructure/
COPY src/HouseFlow.API/HouseFlow.API.csproj HouseFlow.API/
RUN dotnet restore HouseFlow.API/HouseFlow.API.csproj

COPY src/HouseFlow.Core/ HouseFlow.Core/
COPY src/HouseFlow.Application/ HouseFlow.Application/
COPY src/HouseFlow.Infrastructure/ HouseFlow.Infrastructure/
COPY src/HouseFlow.API/ HouseFlow.API/
RUN dotnet publish HouseFlow.API/HouseFlow.API.csproj -c Release -o /app/publish --no-restore

FROM runtime
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "HouseFlow.API.dll"]
