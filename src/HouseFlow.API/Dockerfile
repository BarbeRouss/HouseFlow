FROM mcr.microsoft.com/dotnet/aspnet:11.0@sha256:bc0d6653979efba491ab3d6f589d691f96c309c3c3ef81325efa6978e7b7f242 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:11.0@sha256:65f3c7c6a25a239ab3e6719f2896147e7b68242ff2d8818dd70498bde6ef8bea AS build
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
# HouseFlow.Application.csproj compiles ../../ContractAliases.cs (repo root); the
# projects live under /src, so the file must sit at /ContractAliases.cs.
COPY ContractAliases.cs /ContractAliases.cs
# No --no-restore: the Dapper.AOT source-generator package isn't reliably present
# from the csproj-only restore layer above, so let publish restore the full graph.
RUN dotnet publish HouseFlow.API/HouseFlow.API.csproj -c Release -o /app/publish

FROM runtime
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "HouseFlow.API.dll"]
