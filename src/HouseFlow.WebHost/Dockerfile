# Blazor WebAssembly frontend served by the thin ASP.NET Core host (HouseFlow.WebHost).
# Build context = repo root:  docker build -f src/HouseFlow.WebHost/Dockerfile .
#
# The WASM app is published STANDALONE (not through the host): only the standalone
# publish resolves index.html's fingerprint placeholders (<script type="importmap">,
# blazor.webassembly#[.{fingerprint}].js) — publishing the host alone leaves them empty
# and the app never boots. Its wwwroot is then overlaid on the published host, which
# serves it (with Brotli/gzip negotiation for _framework/*) and exposes the runtime
# config endpoint /appsettings.json driven by API_BASE_URL / DEMO_MODE (see Program.cs).

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:a04d1c1d2d26119049494057d80ea6cda25bbd8aef7c444a1fc1ef874fd3955b AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=3000
EXPOSE 3000

# Tailwind CSS (wwwroot/css/app.css is generated, not committed).
FROM node:22-alpine@sha256:c610fcdfb1d5b4740dd70c284ed3cb16bb857e0f7166196e36a5501df7a3aa32 AS css
WORKDIR /web
COPY src/HouseFlow.Web/package.json src/HouseFlow.Web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY src/HouseFlow.Web/ ./
RUN npm run build:css

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:478b9038d187e5b5c29bfa8173ded5d29e864b5ad06102a12106380ee01e2e49 AS build
WORKDIR /src
COPY src/HouseFlow.Web/HouseFlow.Web.csproj HouseFlow.Web/
COPY src/HouseFlow.WebHost/HouseFlow.WebHost.csproj HouseFlow.WebHost/
RUN dotnet restore HouseFlow.WebHost/HouseFlow.WebHost.csproj

COPY src/HouseFlow.Web/ HouseFlow.Web/
COPY src/HouseFlow.WebHost/ HouseFlow.WebHost/
COPY --from=css /web/wwwroot/css/app.css HouseFlow.Web/wwwroot/css/app.css
RUN dotnet publish HouseFlow.Web/HouseFlow.Web.csproj -c Release -o /app/web
RUN dotnet publish HouseFlow.WebHost/HouseFlow.WebHost.csproj -c Release -o /app/host

FROM runtime
COPY --from=build /app/host .
COPY --from=build /app/web/wwwroot ./wwwroot
ENTRYPOINT ["dotnet", "HouseFlow.WebHost.dll"]
