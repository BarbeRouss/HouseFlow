FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:a04d1c1d2d26119049494057d80ea6cda25bbd8aef7c444a1fc1ef874fd3955b AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:478b9038d187e5b5c29bfa8173ded5d29e864b5ad06102a12106380ee01e2e49 AS build
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
