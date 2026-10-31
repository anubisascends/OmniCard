# OmniCard

A web app for scanning and managing trading card collections. Upload card images (or snap them with your phone) and OmniCard identifies them via perceptual hashing + OCR, then tracks them across storage locations, sealed-product inventory, and sales/fulfillment — all from any device on your network.

Supports **Magic: The Gathering** (Scryfall), **One Piece TCG**, **Riftbound**, **Pokémon**, **Yu-Gi-Oh!**, and **Final Fantasy TCG** (the last three via TCGCSV).

> OmniCard started as a WPF desktop app with a read-only web companion. It has since been rebuilt as a full read/write web app (ASP.NET Core API + React/TypeScript SPA) backed by SQL Server, and the desktop app has been retired — everything now runs through `OmniCard.Web`.

## Features

- **Image scanning** — upload card photos/scans (or use the phone camera); matching runs **server-side** (pHash + foil edge hash + OCR), with a review-and-commit queue
- **Collection management** across storage locations (binders, boxes, deck boxes, bulk), with a visual binder editor
- **Set completion tracking** + printable want-lists
- **Decklist checking** against your collection (Moxfield / Archidekt): printable pull list + missing list, and one-click move into a deck box
- **CSV import/export** (Manabox, Moxfield, TCGplayer, app-native), plus **import a Moxfield / Archidekt deck URL** straight into a location
- **Sealed inventory** (booster boxes, packs, cases…) with lots and valuation
- **Sales & fulfillment** — orders kanban, listings, customers, pick-list & receipt PDFs
- **eBay listing integration** (server-side OAuth)
- **Card lists** and **trade history**
- **Location auditing** with PDF reports
- **Server-hosted artwork** — card images cached on the server and served locally

## Requirements

- **.NET 10 SDK** — <https://dotnet.microsoft.com/download/dotnet/10.0>
- **Node 18+** (to build the SPA)
- **SQL Server 2019+** (Express is fine) — the unified store and per-game catalogs
- **Windows or Linux (x64)** — on Linux the OCR engine comes from the distribution (`libtesseract5`, `liblept5`) and PDF export needs system fonts; the easiest route is the Docker image, see [Docker / Linux](#docker--linux)

## Download

Grab the latest release from the [Releases](../../releases) page:

| Asset | What it is |
|-------|------------|
| `OmniCard-Web-v{VERSION}-iis.zip` | The web app, packaged for installation on IIS |

## Installing on IIS

1. Install the **.NET 10 Hosting Bundle** (provides the ASP.NET Core Module V2) and **SQL Server**.
2. Create an **Application Pool** with **No Managed Code**, and a **Site/Application** pointing at the extracted zip.
3. Configure `DataDirectory`, `ConnectionStrings__OmniCard`, and (optionally) the `eBay__*` keys via `web.config` `environmentVariables` or `appsettings.json`. Sign-in uses per-user accounts (a built-in `Admin` / `admin` account is created on first start — change its password).
4. Run the one-time data migration and grant the app-pool identity the needed permissions.

**Full step-by-step deployment/operations guide — SQL Server setup, data migration, config keys, artwork, and eBay — is in [OmniCard.Web/README.md](OmniCard.Web/README.md).**

## Docker / Linux

OmniCard runs on Linux in Docker: [`Dockerfile`](Dockerfile) builds the SPA (Node) and the backend (.NET SDK)
into an `aspnet:10.0` runtime image (Ubuntu 24.04) that includes the Tesseract OCR libraries and the fonts
QuestPDF needs. It runs as a non-root user (UID 1654) on port 8080. [`docker-compose.yml`](docker-compose.yml) runs it next to SQL Server 2022:

| Service | Image | Volume | Purpose |
|---|---|---|---|
| `omnicard-web` | built from `Dockerfile` | `omnicard-data` → `/data` | the app; `/data` holds `scans/`, `scan-batches/`, `card-images/`, `symbols/`, `branding/`, `dataprotection-keys/`, settings JSON files and `logs/` |
| `mssql` | `mcr.microsoft.com/mssql/server:2022-latest` | `mssql-data` → `/var/opt/mssql` | `OmniCard` + one catalog database per game |

### Setup

Requirements: Docker Engine 24+ with Compose v2, an **x86-64** host, and at least **4 GB of RAM for
the containers** (SQL Server refuses to start with less than 2 GB). On Docker Desktop, Rancher Desktop or WSL2,
give the VM 6 GB or more.

```bash
git clone <this repository> omnicard && cd omnicard
cp .env.example .env
# Edit .env: set MSSQL_SA_PASSWORD (8+ chars, 3 of upper/lower/digit/symbol; avoid ; " ' $)
# and, optionally, OMNICARD_HTTP_PORT, TZ and the eBay__* keys.
docker compose up -d --build
docker compose ps          # both services should become "healthy" (first start: ~1-2 minutes)
```

Then:

1. Open `http://<host>:8080/app/` and sign in as **`Admin` / `admin`**. Change the password at once
   (account menu ▸ *My account*).
2. Go to **Settings ▸ Catalog data**. For each game you use, run **Download catalog**, then
   **Recompute hashes** (and *Download artwork* if you want images served locally).

The databases and their schemas are created on the first start, and EF Core migrations are applied on every start.
Nothing needs to be run by hand. The web container waits for SQL Server's health check before it starts.

**Configuration** — everything is set in `.env` (see [`.env.example`](.env.example)) and passed to the
container as environment variables (`__` = section separator):

| Variable | Default | Purpose |
|---|---|---|
| `MSSQL_SA_PASSWORD` | — (required) | SQL Server `sa` password. **Only applied when the `mssql-data` volume is first created**; to change it later, use `ALTER LOGIN sa WITH PASSWORD = …` and then update `.env`. |
| `MSSQL_PID` | `Express` | SQL Server edition (`Express`, `Developer`, `Standard`, `Enterprise`; Express caps each database at 10 GB). |
| `ConnectionStrings__OmniCard` | `Server=mssql,1433;…;User Id=sa;Password=${MSSQL_SA_PASSWORD}` | Unified store; the per-game catalogs reuse it with the database name swapped. The app needs `dbcreator` rights on its first start. |
| `DataDirectory` | `/data` | Data directory inside the container (keep `/data`, which is where the volume is mounted). |
| `OMNICARD_HTTP_PORT` | `8080` | Host port. Use `127.0.0.1:8080` to listen only locally (behind a reverse proxy). |
| `OMNICARD_VERSION` | `0.0.0` | Version stamped into the image and shown in *About* (the build context has no git history). |
| `Logging__File__Enabled` | `true` | Daily rolling log files in `/data/logs` (14 kept), in addition to `docker compose logs`. |
| `EF_SQL_LOG_LEVEL` | `Warning` | Level of EF Core's per-query SQL logging (`Information` logs every query). |
| `ASPNETCORE_LOG_LEVEL` | `Warning` | Level of ASP.NET Core's per-request logging (`Information` logs every request, health checks included). |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `false` | Set `true` behind a reverse proxy (see below). |
| `Mcp__AllowRemote` | `false` | See *Known limitations*. |
| `TZ` | `Etc/UTC` | Container time zone (log and PDF timestamps). |
| `eBay__AppId`, `eBay__CertId`, `eBay__DevId`, `eBay__RuName`, `eBay__AcceptUrl`, `eBay__Environment` | blank / `sandbox` | Optional eBay integration ([eBay setup](OmniCard.Web/README.md#ebay-setup)). |

**Watched scan folders** — they must live outside the data directory. Uncomment the
`./scans-inbox:/scans` mount in `docker-compose.yml`. Make the host folder writable by UID 1654
(`sudo chown -R 1654:1654 scans-inbox`), since picked-up files are moved to `_processed`. Then configure
per-game folders such as `/scans/Mtg` in *Administration ▸ Scan*.

**Updating** — `git pull && docker compose up -d --build`. Pending migrations are applied on start.

**Logs** — `docker compose logs -f omnicard-web`, or the files in `/data/logs`
(`docker compose exec omnicard-web ls /data/logs`).

**Bringing over an existing Windows installation** — restore SQL Server backups of `OmniCard` and the
`OmniCard_<Game>` databases into the container (see *Restore* below), then copy the old data directory's
contents into the `omnicard-data` volume. The `docker run … tar` restore command below works for a `.tgz` made from that
folder. Data still in the legacy desktop SQLite files can be copied with `OmniCard.DbMigrator` from a .NET SDK
container on the compose network:

```bash
docker run --rm --network omnicard_default -v "$PWD":/src -v /path/to/legacy-data:/legacy:ro -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 dotnet run --project OmniCard.DbMigrator -c Release -- /legacy \
  "Server=mssql,1433;Database=OmniCard;User Id=sa;Password=<MSSQL_SA_PASSWORD>;TrustServerCertificate=True"
```

Stored perceptual hashes stay valid. The Linux build reproduces the Windows (GDI+) hashes exactly for most
images and within a few bits otherwise (see *Known limitations*). Running **Recompute hashes** once after the
move aligns the catalogs exactly.

### Backups

Everything worth keeping lives in the two volumes. **Back up the catalog databases too**: besides the
re-downloadable card data, they hold the scan corrections you made (`HashCorrections`).

**Online (no downtime)** — SQL Server backups plus an archive of the data volume. Run these from the
folder with `docker-compose.yml` and `.env`:

```bash
MSSQL_SA_PASSWORD=$(sed -n 's/^MSSQL_SA_PASSWORD=//p' .env)
mkdir -p backup
docker compose exec mssql mkdir -p /var/opt/mssql/backup
for db in OmniCard OmniCard_Scryfall OmniCard_Optcg OmniCard_Riftbound OmniCard_Pokemon OmniCard_Yugioh OmniCard_FinalFantasy; do
  docker compose exec mssql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -b \
    -Q "BACKUP DATABASE [$db] TO DISK = N'/var/opt/mssql/backup/$db.bak' WITH COPY_ONLY, INIT, CHECKSUM"
done
docker run --rm -v omnicard_mssql-data:/v:ro -v "$PWD/backup":/backup alpine sh -c 'cp /v/backup/*.bak /backup/'
docker run --rm -v omnicard_omnicard-data:/data:ro -v "$PWD/backup":/backup alpine \
  tar czf /backup/omnicard-data.tgz -C /data .
```

**Offline (simplest, consistent file copy)** — stop the stack and archive both volumes:

```bash
docker compose stop
docker run --rm -v omnicard_mssql-data:/v:ro -v "$PWD/backup":/backup alpine tar czf /backup/mssql-data.tgz -C /v .
docker run --rm -v omnicard_omnicard-data:/v:ro -v "$PWD/backup":/backup alpine tar czf /backup/omnicard-data.tgz -C /v .
docker compose start
```

**Restore** — stop the web app, put the files back, restore the databases, start again:

```bash
MSSQL_SA_PASSWORD=$(sed -n 's/^MSSQL_SA_PASSWORD=//p' .env)
docker compose stop omnicard-web
docker run --rm -v omnicard_omnicard-data:/data -v "$PWD/backup":/backup alpine \
  sh -c 'rm -rf /data/* && tar xzf /backup/omnicard-data.tgz -C /data && chown -R 1654:1654 /data'
docker run --rm -v omnicard_mssql-data:/v -v "$PWD/backup":/backup alpine \
  sh -c 'mkdir -p /v/backup && cp /backup/*.bak /v/backup/ && chown -R 10001:10001 /v/backup'
for db in OmniCard OmniCard_Scryfall OmniCard_Optcg OmniCard_Riftbound OmniCard_Pokemon OmniCard_Yugioh OmniCard_FinalFantasy; do
  docker compose exec mssql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -b \
    -Q "RESTORE DATABASE [$db] FROM DISK = N'/var/opt/mssql/backup/$db.bak' WITH REPLACE"
done
docker compose start omnicard-web
```

When restoring a `.bak` taken on **Windows**, add `MOVE` clauses so the files land in
`/var/opt/mssql/data`. List the logical names with `RESTORE FILELISTONLY`, then run e.g.
`WITH REPLACE, MOVE 'OmniCard' TO '/var/opt/mssql/data/OmniCard.mdf', MOVE 'OmniCard_log' TO '/var/opt/mssql/data/OmniCard_log.ldf'`.
The volume archives (`*.tgz`) are restored the same way into an empty volume. `docker compose down -v`
deletes both volumes, so only use it when you really want to start over.

### Reverse proxy and HTTPS

The container speaks plain HTTP on port 8080. For anything beyond a trusted LAN, put a reverse proxy in
front of it that terminates TLS. HTTPS is also **required for the phone camera** (browsers only allow
`getUserMedia` on secure origins), for the eBay OAuth callback and for MCP OAuth.

1. Publish the app only to the proxy: `OMNICARD_HTTP_PORT=127.0.0.1:8080`, or attach the proxy to the
   compose network and drop the `ports:` mapping.
2. Set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` so the app uses the public scheme and host from
   `X-Forwarded-Proto` / `X-Forwarded-For` (secure cookies, eBay `AcceptUrl`, MCP metadata). It trusts any
   proxy, so the container must not be reachable except through the proxy.
3. Allow uploads up to 30 MB (scans) and pass WebSocket upgrades through (`/hubs/scan`, SignalR).

Caddy (automatic Let's Encrypt certificates):

```caddyfile
omnicard.example.com {
    reverse_proxy 127.0.0.1:8080
}
```

nginx:

```nginx
server {
    listen 443 ssl http2;
    server_name omnicard.example.com;
    ssl_certificate     /etc/letsencrypt/live/omnicard.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/omnicard.example.com/privkey.pem;
    client_max_body_size 32m;

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection $connection_upgrade;   # map $http_upgrade $connection_upgrade { default upgrade; '' close; }
        proxy_read_timeout 300s;                           # catalog downloads, large scan batches
    }
}
```

### Known limitations

- **No ARM64.** SQL Server for Linux ships x64 images only. Raspberry Pi, Ampere and Graviton servers
  can't run the `mssql` service, and on Apple Silicon it only runs under x64 emulation (Rosetta), which is
  unsupported and slow. (Azure SQL Edge, the former ARM option, is retired.) On ARM, point
  `ConnectionStrings__OmniCard` at a SQL Server running elsewhere and remove the `mssql` service. The
  `omnicard-web` image is also built for `linux-x64` only; an ARM64 build would need the publish RID changed
  and has not been tested.
- **Perceptual hashes** are computed by a managed re-implementation of GDI+'s resampler instead of
  System.Drawing. Against the original Windows output, about 87% of hashes are identical and the rest differ by
  at most 4 of 64 bits (the match threshold is 14). Nearly blank images can differ more. A card sitting
  exactly on the match threshold can flip between match and no match. *Recompute hashes* makes the catalogs exact.
- **OCR** uses the distribution's Tesseract 5.3 on Linux (5.2 on Windows). Structured reads (set code +
  collector number, editions, The List glyph) agree with Windows, while free text such as card names can differ slightly.
- **Moxfield deck import** shells out to `curl`. Moxfield sits behind Cloudflare, which may block Linux
  curl's TLS fingerprint where Windows' `curl.exe` got through. Archidekt is unaffected.
- **MCP endpoint** (`/mcp`): without OAuth it only answers loopback requests, and inside a container no
  request is loopback, so it returns 403. Configure MCP OAuth (see the web README), or set
  `Mcp__AllowRemote=true` only if your own authentication protects `/mcp`.
- **One instance only**: watched scan folders and the background batch processor assume a single
  container. Don't scale `omnicard-web` beyond one replica.
- **DataProtection keys** (they encrypt the stored eBay tokens and sign-in cookies) are kept unencrypted in
  `/data/dataprotection-keys`. Protect the volume and its backups accordingly.
- The *Watched scan folders* field in the UI still suggests a Windows path (`D:\Scans\…`); on Linux use
  container paths such as `/scans/Mtg`.

## Building from Source

```bash
git clone https://github.com/anubisascends/OmniCard.git
cd OmniCard

# Build the SPA (into OmniCard.Web/wwwroot/app)
cd OmniCard.Web/ClientApp && npm install && npm run build && cd ../..

# Build the backend
dotnet build OmniCard.slnx
```

### Run (development)

Two terminals — the backend serves the API, Vite serves the SPA with hot-reload and proxies `/api`:

```bash
# terminal 1 — backend on :5000
dotnet run --project OmniCard.Web/OmniCard.Web.csproj -- --db "C:\path\to\your\data"

# terminal 2 — SPA dev server on :5173
cd OmniCard.Web/ClientApp && npm run dev
```

Open <http://localhost:5173>. For a production-like check, `npm run build` then browse <http://localhost:5000/app/>.

### Tests

```bash
dotnet test OmniCard.Tests/OmniCard.Tests.csproj
```

### Publish (for IIS)

```bash
cd OmniCard.Web/ClientApp && npm run build && cd ../..
dotnet publish OmniCard.Web/OmniCard.Web.csproj -c Release -r win-x64 --self-contained false
```

For a Linux host without Docker, publish with `-r linux-x64` and install `libtesseract5 liblept5
fonts-dejavu-core fonts-liberation curl` (Debian/Ubuntu package names) on the server.

## SPA pages

The React app is served at `/app`:

| Route | Description |
|-------|-------------|
| `/` | Dashboard — holdings + realized P&L |
| `/scan` | Upload/scan cards; server-side match, review, and commit |
| `/collection` | Searchable collection grid with edit drawer + decklist check (pull/missing PDFs, move to deck box) |
| `/locations`, `/location/:id`, `/binder/:id` | Storage locations + visual binder |
| `/sets` | Set-completion checklist + want-list PDF |
| `/inventory` | Sealed product + lots + valuation |
| `/lists`, `/trades` | Saved card lists; trade history |
| `/import` | CSV import/export + deck URL import |
| `/sales` | Orders kanban, listings, customers |
| `/settings` | eBay connection, catalog refresh, artwork download |

## Tech Stack

| Component | Technology |
|-----------|------------|
| Backend | ASP.NET Core (.NET 10), EF Core |
| Frontend | React + TypeScript + Vite + MUI, TanStack Query |
| Database | SQL Server (unified store + per-game catalogs) |
| Card identification | Perceptual hashing (pHash), OCR |
| Card data | Scryfall API, TCGCSV, poneglyph (One Piece) |
| PDF reports | QuestPDF |
| eBay integration | eBay REST API + OAuth |
| Logging | Microsoft.Extensions.Logging (console) + Serilog (optional rolling files) |

## Project Structure

```
OmniCard.Web/           ASP.NET Core API + React/TS SPA (ClientApp/) — the app
OmniCard.Api.Contracts/ Pure DTO records (the SPA's request/response contract)
OmniCard.Shared/        Shared models and interfaces
OmniCard.Data/          EF Core DbContexts (unified store + per-game catalogs)
OmniCard.CardMatching/  Per-game card services + hash/OCR matching
OmniCard.Collection/    Collection, inventory, sales, decklist, lists, trades
OmniCard.Imaging/       Perceptual hashing, OCR, image caching
OmniCard.eBay/          eBay OAuth, catalog, and listing services
OmniCard.Audit/         Location auditing and PDF export
OmniCard.DbMigrator/    One-time SQLite → SQL Server data copy
OmniCard.Tests/         Unit and integration tests (xUnit)
```

## Data Storage

- **SQL Server** — the unified store (`OmniCard`: collection, inventory, sales) and one catalog DB per game (`OmniCard_Scryfall`, `OmniCard_Pokemon`, …).
- **Data directory** (`--db` / `DataDirectory`; default `%LOCALAPPDATA%\OmniCard` on Windows, `~/.local/share/OmniCard` on Linux, `/data` in Docker) — `scans/`, `card-images/` (server-hosted artwork), `symbols/`, `dataprotection-keys/`, and `logs/` (daily rolling files, 14 kept, when `Logging:File:Enabled` is `true`).

## License

[MIT](LICENSE.txt)
