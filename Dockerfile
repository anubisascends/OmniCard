# syntax=docker/dockerfile:1
#
# OmniCard web app — multi-stage build for Linux (x64).
#   1. node:20      builds the React SPA into OmniCard.Web/wwwroot/app
#   2. sdk:10.0     publishes the ASP.NET Core backend (framework-dependent, linux-x64)
#   3. aspnet:10.0  runtime with the native OCR libraries and fonts, running as a non-root user
#
# Build:  docker build -t omnicard-web --build-arg APP_VERSION=1.2.3 .
# (or `docker compose build`, see docker-compose.yml)

# ---------------------------------------------------------------------------------------------
# 1. SPA
# ---------------------------------------------------------------------------------------------
# Node 20 builds the SPA fine; some packages (@zxing/library 0.23) declare node >= 24 and print an
# EBADENGINE warning — pass --build-arg NODE_VERSION=24 to match the development setup.
ARG NODE_VERSION=20
FROM node:${NODE_VERSION} AS spa
WORKDIR /src/OmniCard.Web/ClientApp
COPY OmniCard.Web/ClientApp/package.json OmniCard.Web/ClientApp/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY OmniCard.Web/ClientApp/ ./
# vite.config.ts emits into ../wwwroot/app (= /src/OmniCard.Web/wwwroot/app).
RUN npm run build

# ---------------------------------------------------------------------------------------------
# 2. Backend
# ---------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# Version stamped into the assemblies (shown in the About dialog). MinVer normally derives it from
# git tags, but .git is not part of the build context.
ARG APP_VERSION=0.0.0
WORKDIR /src

# Restore first, from the project files only, so the package layer is cached across code changes.
COPY Directory.Build.props ./
COPY OmniCard.Shared/OmniCard.Shared.csproj OmniCard.Shared/
COPY OmniCard.Data/OmniCard.Data.csproj OmniCard.Data/
COPY OmniCard.Imaging/OmniCard.Imaging.csproj OmniCard.Imaging/
COPY OmniCard.CardMatching/OmniCard.CardMatching.csproj OmniCard.CardMatching/
COPY OmniCard.Collection/OmniCard.Collection.csproj OmniCard.Collection/
COPY OmniCard.Audit/OmniCard.Audit.csproj OmniCard.Audit/
COPY OmniCard.eBay/OmniCard.eBay.csproj OmniCard.eBay/
COPY OmniCard.Api.Contracts/OmniCard.Api.Contracts.csproj OmniCard.Api.Contracts/
COPY OmniCard.Web/OmniCard.Web.csproj OmniCard.Web/
RUN dotnet restore OmniCard.Web/OmniCard.Web.csproj -r linux-x64

COPY . .
COPY --from=spa /src/OmniCard.Web/wwwroot/app OmniCard.Web/wwwroot/app
RUN dotnet publish OmniCard.Web/OmniCard.Web.csproj -c Release -r linux-x64 --self-contained false \
        --no-restore -p:MinVerVersionOverride=${APP_VERSION} -o /app/publish

# ---------------------------------------------------------------------------------------------
# 3. Runtime
# ---------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# - libtesseract5 + liblept5: OCR engine. The Tesseract NuGet package only ships Windows binaries;
#   OmniCard.Imaging/TesseractNativeSetup.cs maps the wrapper onto these system libraries.
# - fonts: QuestPDF lays out PDF text with system fonts (no fonts = layout failure); Liberation is
#   metric-compatible with Arial/Helvetica, DejaVu covers the wider Unicode range (—, ★, accents).
# - curl: the Moxfield deck import shells out to it; also used by the health check.
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        libtesseract5 liblept5 \
        fontconfig fonts-dejavu-core fonts-liberation \
        curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

# /data  — DataDirectory: scans, scan batches, card images, set symbols, DataProtection keys, settings, logs.
# /scans — optional mount point for watched scan folders (they must live outside the data directory).
RUN mkdir -p /data /scans && chown "$APP_UID:$APP_UID" /data /scans

ENV DataDirectory=/data \
    ASPNETCORE_HTTP_PORTS=8080 \
    Logging__File__Enabled=true

EXPOSE 8080
VOLUME ["/data"]

# Non-root user provided by the .NET images (UID 1654).
USER $APP_UID

HEALTHCHECK --interval=30s --timeout=5s --start-period=90s --retries=3 \
    CMD curl -fsS -o /dev/null http://localhost:8080/app/ || exit 1

ENTRYPOINT ["dotnet", "OmniCard.Web.dll"]
