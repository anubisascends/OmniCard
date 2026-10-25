# OmniCard Web

OmniCard is a React/TypeScript single-page app (Vite + MUI) served by the ASP.NET Core backend in
this project. It is LAN-accessible and IIS-hostable, and a full **read/write** app: browse and edit
the collection, manage locations and binders, scan cards with a phone camera (matching runs
server-side), track sealed inventory, run the sales/orders workflow, and list on eBay. This is the
whole app — the original WPF desktop app has been retired.

## Architecture

- **SPA** — `ClientApp/` (React + TS + Vite + MUI). Built to `wwwroot/app/` and served at **`/app`**
  with a client-side-routing fallback. The API lives under `/api/*`.
- **Unified store on SQL Server** — collection, inventory, sales (`OmniCardDbContext`) live in SQL
  Server for true multi-user concurrency (shadow `rowversion` tokens on the edited entities). EF
  migrations under `Migrations/` are applied automatically at startup.
- **Per-game catalogs on SQL Server** — one database per game (`OmniCard_Scryfall`, `OmniCard_Optcg`,
  `OmniCard_Riftbound`, `OmniCard_Pokemon`, `OmniCard_Yugioh`, `OmniCard_FinalFantasy`). They're
  disposable reference caches (refresh wipes + reloads), so they use `EnsureCreated` at startup, not
  migrations. Refreshed in-place via the catalog "refresh" operations (Settings → Catalog data).
- **Per-user accounts + granular permissions** — two-step sign-in (username **or email**, then
  password; passwords and setup keys stored as salted PBKDF2 hashes). A built-in `Admin` account
  (default password `admin`) is seeded on first run — change it after signing in. Admins never set
  other users' passwords: creating an account or **Require password reset** issues a one-time
  alphanumeric **setup key** the admin hands over; the user enters it at sign-in to choose a password
  (the old password stops working immediately; 5 wrong keys void it). No email is sent. "Remember me" issues a persistent, encrypted auth cookie.
  Every user edits their own email + password on the **Account** page (`/account`, account menu ▸
  **My account**; `/api/account`). Manage accounts under **Administration → Users** and permission bundles under
  **Administration → Roles** (both admin-only). Each app section has granular permissions
  (view/create/edit/delete plus a few special actions); access is granted via a **role** plus
  optional per-user **grant/deny overrides**. Admin accounts (and the built-in `Admin`) hold every
  permission. New non-admin users default to the seeded **Viewer** role (view-only). The API
  enforces each permission per request (`RequirePermission` filter → `PermissionService`), so an
  admin's change takes effect on the affected user's **next request — no re-login**. The permission
  catalog is defined in `OmniCard.Shared/Security/Permissions.cs` (exposed at `GET /api/meta/permissions`).
- **Sites** — a site is a **major physical location** (a home, a shop, a storage unit) that holds many
  storage locations; locations live inside a site (`Site ▸ Location ▸ Card`). Sites let several people
  share one collection while each sees only the places they're allowed to (see [Sites](#sites)).
- **Server-side scanning** — image upload → perceptual hash + OCR matching via the per-game
  `ICardGameService` pipeline; no TWAIN, no desktop agent. Modern MTG frames are identified by the
  printed set code + collector number; pre-2015 ("old") frames, which print neither, are identified by
  title OCR and their printing picked from border colour, the copyright line/year, any printed collector
  number and flavor text (`OmniCard.CardMatching/OldFrame`), since same-art reprints hash identically.
- **Server-hosted artwork** — card images cached under `{dataDir}/card-images`, served at
  `/card-images` (see [Catalog data](#catalog-data)).
- **Localized UI** — every SPA string runs through `react-i18next`; the display language follows the
  visitor's browser culture with **en-US** as the fallback, and numbers/currency/dates are formatted
  per that culture (see [Localization](#localization)).

## Prerequisites

- **.NET 10 SDK** (build) / **.NET 10 Hosting Bundle** (IIS host).
- **Node 18+ / npm** for the SPA build (developed on Node 24 / npm 9).
- **SQL Server 2019+** (Express is fine). Default connection targets `localhost` with Windows auth.
- A **data directory** (default `%LOCALAPPDATA%\OmniCard`, or `--db <path>`) for `scans/`,
  `card-images/`, `symbols/`, and `dataprotection-keys/`. A fresh server builds the SQL Server DBs
  itself; to bring existing data over, run the one-time migration below.

## Configuration

Settings come from `appsettings.json` (and the shared `%LOCALAPPDATA%\OmniCard\appsettings.json`,
loaded automatically if present). Key settings:

| Key | Purpose |
|-----|---------|
| `DataDirectory` (or `--db <path>` CLI arg) | Folder holding `scans/`, `card-images/`, `symbols/`, `dataprotection-keys/`. |
| `ConnectionStrings:OmniCard` | SQL Server unified store. Default: `Server=localhost;Database=OmniCard;Trusted_Connection=True;TrustServerCertificate=True;` The per-game catalog DBs reuse this with the database name swapped (or set `ConnectionStrings:OmniCard_<Game>` explicitly). |
| `Binder:EditPassphrase` | Legacy binder-editor gate (separate from the per-user sign-in). |
| `eBay` section | eBay app credentials — see [eBay](#ebay-setup). |

DataProtection keys (used to encrypt stored eBay tokens) are persisted to
`<DataDirectory>/dataprotection-keys` so they survive app-pool recycles.

## First-time data migration (SQLite → SQL Server)

If you have existing data from the old SQLite files (`inventory.db` + the per-game catalogs
`scryfall.db`, `optcg.db`, …) in a data directory, copy it all into SQL Server once with the bundled
migrator. It's idempotent (clears each target first, preserves ids), copies the unified store **and**
every per-game catalog (creating the `OmniCard` + `OmniCard_<Game>` databases as needed), and streams
in batches so large catalogs (Scryfall is 100k+ cards) don't blow memory. Expect it to take a while.

```bash
dotnet run --project OmniCard.DbMigrator -c Release -- "<dataDir>" ["<sqlserver-connstring>"]
# e.g.
dotnet run --project OmniCard.DbMigrator -c Release -- "X:\TCG Card Scanner"
```

A fresh install with no prior data can skip this — the web app creates the databases on startup, and
you populate the catalogs via **Settings → Catalog data** (Download catalog / Update prices).

On startup the web app applies any pending EF migrations (creating the `OmniCard` database if
absent). To add a schema migration later (local `dotnet-ef` tool 10.0.9 is pinned in
`dotnet-tools.json`):

```bash
dotnet dotnet-ef migrations add <Name> -p OmniCard.Web -s OmniCard.Web -c OmniCardDbContext -o Migrations
```

## Build

```bash
# 1. Build the SPA into wwwroot/app
cd OmniCard.Web/ClientApp
npm install
npm run build

# 2. Build (or publish) the backend
cd ../..
dotnet build OmniCard.Web/OmniCard.Web.csproj
```

`ClientApp/**` is excluded from the .NET globs, so `node_modules` never ends up in a publish; the
built `wwwroot/app` output does. `wwwroot/app` is build output and is git-ignored, so a fresh clone
has no SPA until you run `npm run build` (the release/nightly workflows do this before publishing).

## Run (development)

Two terminals — the backend serves the API, Vite serves the SPA with HMR and proxies `/api`, `/hubs`,
`/scans`, `/openapi` to the backend:

```bash
# terminal 1 — backend on :5000
dotnet run --project OmniCard.Web/OmniCard.Web.csproj -- --db "X:\TCG Card Scanner"

# terminal 2 — Vite dev server on :5173
cd OmniCard.Web/ClientApp && npm run dev
```

Open http://localhost:5173. For a production-like check, `npm run build` then browse
http://localhost:5000/app/.

> Gotcha: `dotnet run` spawns `OmniCard.Web.exe`, which lingers and locks the DLLs. Run
> `taskkill /F /IM OmniCard.Web.exe` before rebuilding.

## Publish & IIS

```bash
# Build the SPA first (populates wwwroot/app), then publish with an explicit RID:
dotnet publish OmniCard.Web/OmniCard.Web.csproj -c Release -r win-x64 --self-contained false
```

> **Always pass `-r win-x64 --self-contained false`.** A RID-less framework-dependent publish copies
> the native assets for *every* platform (win-x64/x86/arm64 + a dozen linux/osx/… RIDs) into a
> `runtimes/` folder — ~500 MB of dead weight for a single-target IIS box. Pinning the RID emits only
> win-x64. The project also strips the large native debug-symbol files (`libSkiaSharp.pdb` ~89 MB,
> `libHarfBuzzSharp.pdb` ~23 MB) that the SkiaSharp/HarfBuzz packages ship — see
> `CopyDebugSymbolFilesFromPackages` and the `StripNativePdbsFromPublish` target in
> `OmniCard.Web.csproj`. Together these keep a publish around **~125 MB** instead of ~700 MB.

Deploy the published output to IIS:

1. Install the **.NET 10 Hosting Bundle** on the server (provides the ASP.NET Core Module V2).
2. Create an **Application Pool** with **No Managed Code**.
3. Create a **Site/Application** pointing at the published folder, using that app pool.
4. Set config via `web.config` `environmentVariables` (or `appsettings.json`): `DataDirectory`,
   `ConnectionStrings__OmniCard`, and the `eBay__*` keys as needed.
5. Grant the app-pool identity (`IIS AppPool\<name>`):
   - **read/write** on the data directory (`scans/`, `scan-batches/`, `card-images/`, `dataprotection-keys/`),
   - **Modify** on each [watched scan folder](#watched-scan-folders) (picked-up files are moved to `_processed`),
   - access to SQL Server (or use a SQL login in the connection string instead of Windows auth).
6. If you use watched scan folders, keep the app running (see [Keep the app running](#keep-the-app-running-iis)).

> **Wipe the target folder before copying a new build over it.** Neither `dotnet publish`/xcopy nor
> Web Deploy with `SkipExtraFilesOnServer` (this project's publish profiles) delete files that no
> longer exist in the source, so republishing into the same site folder accumulates stale,
> content-hashed SPA chunks (each build adds a fresh ~10 MB `opencv` asset the old `index.html` no
> longer references). The `.vscode/deploy.ps1` script (VS Code **Publish** task) automates this: it
> stops the app pool to release the DLL locks, clears the site folder, publishes, then restarts the
> pool. If you deploy by hand, do the same. The source `wwwroot/app` stays clean on its own — Vite's
> `emptyOutDir` rebuilds it from scratch each time.
>
> The publish profiles under `OmniCard.Web/Properties/PublishProfiles/` pin `RuntimeIdentifier` to
> `win-x64`, so the Web Deploy path produces the same slim, single-RID output as the command above.

### Publish to a remote server (VS Code task)

The VS Code task **Publish to Remote Server** (`Ctrl+Shift+P` ▸ *Tasks: Run Task*) runs
`.vscode/deploy-remote.ps1`. It builds the SPA, publishes to a local staging folder
(`-r win-x64 --self-contained false`) and replaces the remote site's files with the build. It
deletes stale files, keeps the ASP.NET Core Module `logs\` folder, and takes the app offline while
copying.

Settings: `.vscode/deploy-remote.json` holds the tracked defaults. The first run copies it to
`.vscode/deploy-remote.local.json`, which is git-ignored, then stops so you can fill in your server.
Any key you leave out of the local file falls back to the default.

| Key | Meaning |
| --- | --- |
| `Server`, `SiteName`, `SiteUrl` | Server host/IP, IIS site name, URL printed at the end. `{Server}`/`{SiteName}` expand in URLs and paths. |
| `Method` | `Share` mirrors the build into a network share of the site folder (`SharePath`). `WebDeploy` syncs through msdeploy (`WebDeployUrl`, `WebDeployAuthType`, `MsDeployPath`). |
| `UserName` | Server account. Leave it blank to use your Windows login. The password is prompted for; with `RememberPassword` it is saved DPAPI-encrypted (your Windows account on this PC only) in `.vscode/deploy-remote.local.secret`. Delete that file if the password changes. |
| `PreserveSettings` | Top-level `appsettings.json` sections the server keeps between deploys (default `DataDirectory`, `ConnectionStrings`, `eBay`, `Mcp`). New repo keys still flow through. If the server's copy can't be read, it is left untouched. |
| `BuildSpa` | Set to `false` to skip `npm install` + `npm run build`. |
| `ServiceAdminUserName`, `ServiceName`, `ServerAppPath` | Only for **Publish to Remote Server (service)**, below: an administrator account on the server (blank = `UserName`; its remembered password goes in `.vscode/deploy-remote.local.admin.secret` when the names differ), the service name (default `OmniCard`), and the app folder as the server sees it (blank = the folder behind `SharePath`'s share). |

One-time server setup is the same as above (Hosting Bundle, app pool, site, SQL Server, data-directory
grants). In addition:

- **Share:** share the site's physical folder and give the deploy account **Change** permission on
  both the share and the NTFS folder. As a safety check, the script refuses to mirror into a folder
  that is not empty and has no `web.config`. `web.config` is replaced on every deploy, so put
  server-specific config in `appsettings.json`.
- **WebDeploy:** Windows Server uses the Web Management Service (`https://<server>:8172/msdeploy.axd`,
  Basic auth). Install the **IIS Management Service** feature, enable remote connections, start
  `WMSVC`, install Web Deploy with the **IIS Deployment Handler**, and open TCP 8172. On a Windows
  client OS, the Web Deploy **Remote Agent Service** is the alternative:
  `http://<server>/MSDEPLOYAGENTSERVICE`, NTLM, with an admin account on the server.

### Run as a Windows service (instead of IIS)

IIS on a Windows **client** OS (Windows 10/11 Pro) runs at most 10 requests at once, so a page full of
card images queues behind itself and every open tab's scan-hub connection holds a slot. Windows
Server's IIS has no such cap. On a client OS, run the app as a Windows service on its own web server
(Kestrel) instead. Every publish ships `install-service.ps1` for this. On the server, in an elevated
PowerShell in the deployed app folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\install-service.ps1            # switch from IIS to the service
powershell -ExecutionPolicy Bypass -File .\install-service.ps1 -Uninstall # back to IIS
```

It finds the IIS site whose root folder is the one the script runs from (or `-IisSiteName`), reuses
that site's ports and HTTPS certificate (or pass `-HttpPort`, `-HttpsPort`, `-CertificateThumbprint`),
then stops the site and its app pool and turns off their auto-start. If IIS is installed but no site
serves the folder, it lists the sites and stops rather than guessing; it also stops if a port is still
taken after IIS is stopped. It creates the service (`-ServiceName`, default `OmniCard`; delayed auto-start,
restarts after a crash, depends on a local SQL Server instance) under the virtual account
`NT SERVICE\<name>`. It grants that account read on the app folder, modify on `DataDirectory` and the
watched scan folders, read on the certificate's private key, and `db_owner` on the OmniCard databases
plus `dbcreator`, for a local SQL Server using Windows auth. It opens the ports in Windows Firewall for
the local subnet, then starts the service and checks it answers. Re-run it to change ports or the
certificate; it updates the existing service.

- The endpoints live with the service, not in `appsettings.json`, so deploys don't touch them. They're
  in the service's registry `Environment` value: `Kestrel__Endpoints__Http__Url`,
  `Kestrel__Endpoints__Https__Url`, `HttpsCertificateThumbprint` (a `LocalMachine\My` certificate, loaded
  by `WindowsServiceHosting`) and `WindowsService__Name`.
- **Deploys work unchanged** with the `Share` method. The service honors `app_offline.htm` like the
  ASP.NET Core Module does (`AppOfflineWatcher`): it stops so its DLLs can be replaced, and a detached
  helper starts it again once the file is removed. The installer lets the service account start its
  own service for this. `WebDeploy` needs IIS, so use `Share`.
- **Publish to Remote Server (service)** deploys and (re)installs the service in one step, without
  logging in on the server. It connects first over PowerShell remoting as `ServiceAdminUserName`, so a
  bad setting fails before the build. Then it stops the service, mirrors the build over the share, and
  runs `install-service.ps1` from the app folder on the server, elevated, printing its output in the
  task terminal. The first run also switches an IIS site over to the service. Share method only.
  One-time setup:
  - On the server, in an admin PowerShell: `Enable-PSRemoting -Force -SkipNetworkProfileCheck`. On a
    workgroup PC this also lets local admin accounts keep their admin rights over the network.
  - On this PC, in an admin PowerShell, if the server is an IP address or isn't in your domain:
    `Start-Service WinRM; Set-Item WSMan:\localhost\Client\TrustedHosts -Value '<server>' -Concatenate -Force`.
- The app is always running, so [Keep the app running](#keep-the-app-running-iis) doesn't apply.
  Errors go to the Windows **Application** event log rather than `logs\stdout`.

**Caching + compression (any host):** API responses and the SPA are Brotli/gzip-compressed (the ~1.6 MB
bundle goes out as ~450 KB). Content-hashed `/app/assets/*` files are cached for a year
(`immutable`), `index.html` is always revalidated so a deploy shows up on the next load, and
`/card-images` are cached for a day. SignalR (`/hubs`) and `/mcp` are not compressed.

## Catalog data

The per-game card catalogs, prices, and image hashes are refreshed **server-side** — the web app no
longer needs the desktop to keep them current. In **Settings → Catalog data**, pick a game and run:

- **Update prices** — refresh market prices.
- **Download catalog** — pull the latest card data (bulk).
- **Recompute hashes** — rebuild perceptual hashes used for scan matching.

- **Download artwork** — cache every printing's image to `{dataDir}/card-images` (served locally).

One job runs at a time; progress is shown live. The per-game catalog databases (and their schemas)
are created on first run, so a fresh server with an empty SQL Server instance can build them from
scratch. Columns added in later versions (e.g. the catalog `Lang` column) are added to existing
catalog DBs automatically at startup.

**Sources.** MTG comes from Scryfall. Every other game — One Piece, Riftbound, Pokémon, Yu-Gi-Oh!,
Final Fantasy TCG — comes from [TCGCSV](https://tcgcsv.com) (TCGplayer data, republished daily).
One Piece and Riftbound used to come from poneglyph / Riftcodex. Their existing catalog rows are matched
by TCGplayer product id and keep their ids (so owned copies are unaffected) and their own card data, which
is more accurate than TCGplayer's listing; TCGCSV only gives them prices and fills fields they lack. Rows
TCGCSV created are fully refreshed on every run. Printings TCGCSV doesn't list are kept, never deleted. The first One Piece refresh after upgrading links the existing rows to
TCGplayer once, using poneglyph's `tcgplayer_url` (if poneglyph is unreachable the refresh fails without
changing anything — retry later). New One Piece printings get `{number}` / `{number}_t{productId}` ids;
new Riftbound ones `tcgcsv-{productId}`.

### Automatic updates

**Settings → Catalog data → Automatic updates** schedules the refresh: times of day (server local
time), weekdays, games, and whether to update card data (**Download catalog**), prices, or both. At
each time the app runs each game's jobs one after another through the same job runner (queued behind a
manual job if one is running). Anyone with the catalog view permission sees the schedule and the last
run's result; only **admins** can save it or click **Run now**. A slot missed while the app was down
runs once at the next start (within a week). The schedule is stored in
`{dataDir}/catalog-schedule.json`, the last run in `catalog-schedule-state.json`.
API: `GET /api/catalog/schedule`, `PUT /api/catalog/schedule` and `POST /api/catalog/schedule/run`
(admin). Under IIS the app must stay running for the schedule to fire — see
[Keep the app running](#keep-the-app-running-iis).

### Card languages

Every owned copy carries a **language** (`InventoryLot.Language`; null = English). It's set on the
Scan page (a session **Card language** picker plus per-card/bulk overrides), editable in the card
editor and collection bulk edit, shown as a `JA`-style chip in the collection grid, searchable with
`lang:ja`, round-tripped through native/ManaBox CSV (Moxfield/TCGplayer `Language` columns are read on
import), and sent to eBay as the *Language* item specific.

**Languages to download** (Settings → Catalog data, per game) controls which non-English printings
the catalog holds. English is always included; changes apply on the next **Download catalog**, which
also prunes rows for languages that were un-ticked. The selection is stored in
`{dataDir}/catalog-languages.json`.

| Game | Downloadable | Source | Notes |
|---|---|---|---|
| MTG | all Scryfall languages | Scryfall `all_cards` (~400 MB gz) instead of `default_cards` once any non-English language is ticked | Same set + collector number as English; prices fall back to the English printing; placeholder scans borrow English art |
| One Piece | English only | TCGCSV (English) | Japanese/French rows downloaded earlier from poneglyph (`OP01-001@ja`) are kept, with an English price fallback |
| Pokémon | en, ja | TCGCSV category 85 "Pokemon Japan" | Separate sets (codes prefixed `JP-`), Japanese art, **real prices** |
| Yu-Gi-Oh!, FFTCG, Riftbound | English only | — | Copies can still be tagged with any printed language |

When scanning, a copy's language is: the language **printed on the card** where OCR can read it (MTG's
`• JP` marker next to the set code; Yu-Gi-Oh!'s `-DE`/`-JP` region code, which is also swapped to `-EN`
so the English catalog matches), else the session's **Card language**, else the matched printing's own
language. With a language chosen, the match is remapped to that language's printing when the catalog
holds it.

## eBay setup

The full desktop eBay stack runs server-side; the OAuth flow is a normal web redirect.

1. Fill the `eBay` section (from the eBay developer portal):
   ```json
   "eBay": {
     "AppId": "…", "CertId": "…", "DevId": "…",
     "RuName": "…", "AcceptUrl": "https://<host>/api/ebay/callback",
     "Environment": "sandbox"
   }
   ```
2. In the eBay dev portal, set the RuName's **Auth accepted URL** to `https://<host>/api/ebay/callback`,
   matching `Environment` (sandbox vs production hosts differ).
3. In the app, go to **Settings → eBay → Connect to eBay**, approve consent, then **Run seller setup**.

Until configured, `GET /api/ebay/status` reports what's missing and all listing operations no-op
(so order status changes keep working without a live connection). Tokens are stored encrypted in
`<DataDirectory>/web-credentials.dat`.

## Deck URL import (Moxfield / Archidekt)

**Import / Export → Import from Moxfield / Archidekt** takes a *public* deck URL
(`moxfield.com/decks/{id}` or `archidekt.com/decks/{id}`, MTG only) and adds every card to the chosen
location as owned lots in the chosen condition. Each line keeps its exact set + collector printing and its
foil / etched finish. A printing that isn't in the catalog falls back to the cheapest printing of the name,
and the result lists those cards along with any that couldn't be found. Moxfield maybeboards and Archidekt
categories marked "not in deck" are skipped. "Skip duplicates" skips printings you already own in that
condition.

- API: `POST /api/import/url` (`ImportRun` permission).
- Moxfield's API sits behind Cloudflare, which blocks .NET's `HttpClient`, so the server fetches it by
  running Windows' built-in **`curl.exe`**. It must be on the server's `PATH` (it is by default on Windows
  10 1803+ / Server 2019+). The server needs outbound HTTPS to `api2.moxfield.com` and `archidekt.com`.

## Export scans without adding them

On **Scan** (and a location's **Audit**), **Export** downloads the checked, matched scans without
writing anything to the collection. The scans stay in the list so they can still be added later.
Confirming isn't required. Every collection format is offered: OmniCard, TCGplayer, Moxfield, ManaBox,
Archidekt, Deckbox, Dragon Shield, Card Price Ticker and a plain text list. The Moxfield, ManaBox,
Archidekt and Deckbox formats are MTG-only. The menu can also **copy the text list** to the clipboard
("4 Lightning Bolt (2X2) 117 *F*"), or **export several formats** as one `.zip`. Each copy is written as
its own row, and prices are live market prices.

- API: `POST /api/scan/export` (`ExportRun` permission). One format returns that file; several return a zip.
- The staged scan list (images included) is mirrored to the browser's IndexedDB, so a refresh or crash
  restores it with a "Restored N scans" banner. Scan and each location's audit keep separate sessions. The
  data stays in that browser and is cleared as scans are added, removed or discarded.

## Watched scan folders

A scanner can save straight into a folder on the server, and OmniCard picks the images up and matches them
in the background. Set it up in **Administration ▸ Scan ▸ Watched scan folders** (admins only): turn on
**Watch folders**, give each game a folder on the server, and optionally set the batch defaults (art-fallback
sets, condition, language, foil, default location).

- **Layout:** `<game folder>\<batch name>\*.jpg|png|tif`. Each top-level subfolder is one batch, named after
  the folder; deeper subfolders belong to it. Images saved directly into the game folder go into a batch
  named after the date. Folders starting with `_` or `.` are ignored.
- **Hands off until it starts:** a batch shows up on the Scan page (*Waiting*, with its file count) as soon as
  files appear, but its files are left untouched in the folder: only directory entries (names, sizes,
  timestamps) are read, never the files, so the scanner software can keep writing, renaming and completing
  its run. The batch **starts** when someone clicks **Run** (`POST /api/scan/batches/run`, status
  `Starting`), or by itself (see below). Only then are its files copied to
  `{DataDirectory}\scan-batches\{batchId}\` and the originals moved to `<game folder>\_processed\<batch name>\`.
  A file that's empty or still held open by another program at that moment is left in place.
- **Quiet period:** with automatic processing on, a batch starts once its files have gone the quiet period
  (default 90 s) without any change: a new, renamed, resized or removed file restarts it. Every batch counts
  down on its own, so several batches can be waiting and starting at once; matching itself is one shared
  queue. After a restart, waiting batches start their quiet period over. Files that land in a batch's folder
  after it has started (or were left behind because they were locked) form a new batch named `Name (2)`.
  If a waiting batch's files are removed before it starts, the batch disappears.
- **Automatic processing:** on by default. Turn off **Process batches automatically** (`AutoProcess` in
  `scan-folder-settings.json`) and each batch waits as *Waiting to run* until someone clicks **Run** on the
  Scan page.
- **Originals:** kept in `_processed` by default; clearing it out is up to you. Turn on **Delete originals
  when a batch is closed** (`DeleteOriginalsOnClose`) to delete a batch's originals (each item's
  `ProcessedPath`) when it's committed or discarded, plus any folders under `_processed` that leaves empty.
- **Duplicates:** every picked-up file is SHA-256 hashed (`ScanBatchItems.ContentHash`). A file byte-for-byte
  identical to an open or committed item of the same game (scanner software re-saving a run) is recorded as
  a `Duplicate` row: no stored image, never matched or committed; the original is still moved to
  `_processed`. The batch page folds them into repeated runs ("0001–1000 repeat 0001–1000"). A batch that
  holds only duplicates closes itself as Discarded as soon as it starts.
- **Review:** the **Scan** menu item shows how many batches nobody has opened yet, and the Scan page lists
  every batch with its progress. **Open** claims a batch for you; anyone else sees "In review by …" and can
  only look. The owner (or an admin) can **Release** it; an admin can **Take over**. Edits are saved on the
  server, so a batch can be continued from another browser. **Add confirmed cards** commits from the
  server's copy (site write access applies, as for normal scans); the batch closes when nothing is left.
- **Retention:** images of committed or discarded batches are deleted after the retention period (default
  14 days).
- Matching runs one image at a time in the background, so it doesn't slow down interactive scanning much.
  An unfinished batch carries on after a restart.
- API: `GET /api/scan/batches` (+ `/count`, `/{id}`, `/{id}/items/{itemId}/image`), `POST /run`, `/{id}/claim`,
  `/release`, `/commit`, `/discard`, `/items/remove`, `/items/rematch`, `PUT /{id}/items`. Settings:
  `GET|PUT /api/settings/scan-folders` (admin).
- Assumes **one** app instance: a web garden or several servers would each watch the folders.

### Keep the app running (IIS)

Folders are only watched — and scheduled catalog updates only run — while the app is running. By default IIS stops an idle app and starts it on the
next request, so scans would sit unnoticed until someone opens the site. To keep it running:

1. Install the IIS **Application Initialization** feature.
2. On the app pool: **Start Mode** = `AlwaysRunning`, **Idle Time-out** = `0`. Consider turning off the
   periodic recycle, or schedule it for a quiet time.
3. On the site/application: **Preload Enabled** = `True`.

Folders are rescanned when the app starts, so anything saved while it was stopped is still picked up.

## Import into a location (all-or-nothing)

A location's page has an **Import** button that takes a collection CSV (OmniCard, TCGplayer, Moxfield or
ManaBox) or a Moxfield / Archidekt deck URL and adds every line to *that* location. It's stricter than the
Import / Export page:

- **No location choice.** Every card goes into the current location, whatever location the file names.
  Binder page/slot positions from the file are dropped, so the cards arrive unplaced. A deck-box section is
  kept only when importing into a deck box.
- **No duplicate skipping.** Every line is imported, even if you already own that card.
- **One record per physical card.** A line with quantity 4 creates 4 separate lots of 1, so each copy can
  be placed, listed or moved on its own.
- **All or nothing.** Every line has to parse cleanly and match a real catalog card before anything is
  written. Rows with an ID (OmniCard, ManaBox) are checked by ID. Other rows are matched by name plus
  set/collector number (TCGplayer by set name). Game-locked deck boxes reject other games. If any line
  fails, including an unrecognized condition or a bad quantity, nothing is imported. The user sees every
  problem, with its CSV row number, card name and a fix.

A printing that isn't in the catalog is imported as another printing of the same card, as the lenient URL
import does, and the success message lists those lines.

- API: `POST /api/import/location/{id}/csv` (multipart `file`) and `POST /api/import/location/{id}/url`
  (`{ url, condition }`), both `ImportRun`. A rejected import returns **422** with
  `{ error, errors: [{ row, card, message }] }`.
- CSV quantities are now read on every import (`Quantity` for TCGplayer/ManaBox, `Count` for Moxfield). The
  OmniCard export writes a `Quantity` column, so a stacked lot round-trips.

## Lists: print and put away

Each list on the Lists page shows how many copies of every line are already **owned**. Owned means the
same printing and finish (foil / non-foil) in a site you can read. With the list's **card language** set
to *Any language* (the default), a copy of that printing in any language counts (MTG matches set +
collector number, One Piece ignores the `@lang` suffix). Set a language to count only copies in it. A copy
that's flagged missing or traded doesn't count. Neither does one that's listed for sale or kept in an
**ignored location**, not even a card added from that very lot. The row shows those copies as "+N
ignored". Otherwise a card added from the collection always counts its own copy.

- **Ignored locations** (a sales binder, a deck in use, a whole site…): lists never count or pull cards
  from them, and decklist checks skip them too. Toggle one from its ⋮ menu on the Locations page
  (*Ignore for lists*). Toggle many at once in **Locations ▸ Ignored locations…**, which is also linked
  from *Put cards away* and *Find in collection*. That dialog groups locations by site and type, with a
  tick-box per site and per type. Ignored locations get an *Ignored* chip on the Locations page. *Find in
  collection* still lists their copies, greyed out with the reason (*Ignored location* / *Listed for
  sale*), and they can't be chosen. Stored as `StorageContainer.ExcludeFromDeckCheck`; bulk API
  `PUT /api/locations/ignore-for-lists` with `{ ids, value }` (`LocationsEdit`, write access to every
  location's site).

- **Card language**: chosen when importing a list and changeable on the list. New cards created when
  putting the list away use it, on that language's catalog row when the catalog has one. With *Any
  language* they're created in English.
- **Update from URL**: imported lists remember their Moxfield / Archidekt URL. Update fetches the deck
  again and lists every change (added, removed, quantity changed) with a tick-box. Nothing changes until
  you apply the ticked rows. Removing a card that wasn't imported from the URL starts unticked. Copies
  already moved off the list by *Put cards away* aren't tracked, so an update offers to add them back.
- **Find in collection**: for cards the collection doesn't have in the exact printing, it lists owned
  copies of other printings with the same name (in the list's language), with suggested amounts. Approved
  copies replace the missing ones on the list as stand-ins (shown with a swap icon). A later update from
  the URL counts a stand-in toward the card it replaces.

- **Print ▾**: the whole **list** (quantities, owned counts, prices, totals), a **pick list** (the owned
  copies to pull, grouped by location in section/page/slot order) or a **buy list** (what's missing, with
  prices). Every print has tick-boxes.
- **Put cards away**: choose where the **owned** cards go and where the **new** cards go (picking one fills
  in the other if it's still empty). Then click **Move owned**, **Add new** (new lots in the chosen
  condition) or **Move & add** to do both at once. Moving splits a larger stack so only the needed copies
  move. Done copies come off the list, and an emptied list is deleted. After a move-only run, any shortfall
  stays on the list as *to buy*, so the copies you just moved aren't counted against it again.
- API: `GET /api/lists/{id}/print.pdf`, `/pick-list.pdf`, `/buy-list.pdf` (`ListsView`), and
  `POST /api/lists/{id}/fulfill` with `{ moveToContainerId?, addToContainerId?, condition }`
  (`ListsCommit`). It needs write access to both targets and to every site an owned copy moves out of.
  Both targets are checked first, including deck-box game locks, so a rejected request changes nothing.
  `PUT /api/lists/{id}/language` (`{ language }`, null = any), `POST /api/lists/{id}/update-preview`
  (`{ url? }`) then `/update-apply` (`{ url, rows }`), and `POST /api/lists/{id}/substitutes` then
  `/substitutes/apply` (`{ substitutions: [{ itemId, lotId, quantity }] }`).

## Sites

A **site** is a MAJOR physical location — a home, a shop, a storage unit — that contains many child
storage locations (binders, boxes, deck boxes). It is *not* a shelf, a binder or a box; it's the place
those things live. The hierarchy is **Site ▸ Location ▸ Card**, and every location belongs to exactly
one site.

Use sites when several people share one OmniCard database. Example: *Andrew's House* and *Partner's
House* are two sites; the children's role can **read** Andrew's House but sees nothing of Partner's
House, and Partner's account doesn't see the children's own site.

- **Default site.** Every database has one Default site (id 1, seeded by the `AddSites` migration). It
  is **always visible and writable for every signed-in user** (normal section permissions still apply),
  holds the system **Bulk** location, and is where new locations land unless another site is chosen.
  It can be renamed but never deleted. The migration assigned every pre-existing location to it.
- **Administration ▸ Sites** (administrators only) creates, renames, describes and deletes sites
  (deleting moves the site's locations — with their cards — to a site you choose), and sets **who can
  see each site**: per **role** and per **user**, `None` / `Read` / `Write`. A user's level is the higher
  of their own grant and their role's grant; administrators always see every site. Changes apply on the
  user's next request (no re-login).
- **Read** = browse and search the site's locations and cards, export them, and add them to lists.
  **Write** = also change them (edit/move/delete cards, scan or import into its locations, edit binders,
  create/rename/delete locations in it, put lists away into it).
- **Locations page:** a **Site** filter next to the game filter — *All Sites* (every site you can read,
  the default), then *Default*, then your other sites. View-only locations show a lock and their
  actions are hidden. Locations can be created in, or moved to (*⋮ ▸ Move to site…*), any site you can
  write to.
- **Enforcement** is server-side: collection search/export, location lists, binders, decklist checks,
  lists, trades and scan/import commits only see readable sites (an unreadable location/card answers
  **404**), and writes need write access to every site involved (**403** otherwise) —
  `RequireSiteAccessAttribute` + `RequestSiteAccess` → `SiteAccessService`. Sales listings/orders and
  sealed-product inventory are **not** site-scoped (gate them with section permissions).
- API: `GET /api/sites` (the caller's visible sites + access level); admin-only `POST/PUT/DELETE
  /api/sites[/{id}]` (`DELETE ?moveToSiteId=`), `GET/PUT /api/sites/{id}/grants`;
  `PUT /api/locations/{id}/site`; `siteId` query param on `GET /api/locations`, `GET /api/collection`
  and `GET /api/export/collection`; `siteId` on `POST /api/locations`.

## Saved views

The Collection page and every location page have a **view button** beside the search box that saves
the card list's layout: search query, sort, rows per page, *Stack duplicates*, hidden columns + column
order (the **Columns** toolbar button; the grid's community edition can't drag-reorder), table row
grouping (`groupColumns`, the **Group by** toolbar button), and on a location page table/stacked
display + the stacks' *Group by*.

**Row grouping** nests the card table by Set / Rarity / Condition / Language / Foil / Game (All Games
only) / Location (Collection page only) / Listing status, outermost first. The grid's community edition
has no row grouping, so the server does it: `POST /api/collection/grouped` (`CollectionGrouping`)
groups the whole filtered set, flattens it to header + card rows and pages that sequence (headers carry
copies + market value; a page starting mid-group repeats its headers as *continued*). It loads and prices
the whole filtered set per request, like a market-price sort. Groups open expanded; collapse state
(*Collapse all*, per-group toggles) is per visit and not saved.

- **Scope:** a view belongs to one page — the Collection page, one location, or (admin-shared only) *all
  locations* — and to a game key: one game, the *All Games* selection (`all`), or any game. Personal
  views are private; administrators can publish **shared** views everyone can pick (read-only to others).
- **Defaults:** a page opens with the user's default for the selected game → their any-game default →
  everyone's default (admin-set, shared views only; a location's own beats an *all locations* one) →
  the built-in layout. Setting an any-game default also clears that page's default for the game on
  screen. `?view=<id>` in the page URL opens a specific view (switching the game if it belongs to another).
- **Copy to other locations…** copies a location's view (overwriting same-named views) and can make it
  the default there.
- Storage: `SavedViews` + `SavedViewDefaults` tables (migration `AddSavedViews`; layout JSON in
  `StateJson`). Views cascade with their user/location; deleting a user or location also drops their
  default rows (`SavedViewService.RemoveUser/RemoveLocation`).
- API (`ViewsController`, needs `collection.view` / `locations.view` + site read access for location
  views): `GET /api/views?page=&containerId=&game=`, `GET/PUT/DELETE /api/views/{id}`, `POST /api/views`,
  `PUT/DELETE /api/views/{id}/default?page=&containerId=&game=&everyone=`, `POST /api/views/{id}/copy`.

## Tag rules

Administrators define per-game **tag rules** under *Administration ▸ Tag Rules*: a search query plus
the tags to add. Rules are **additive only** (they never remove a tag).

- **Scans:** the Scan page (and batches/audits, which reuse it) asks `POST /api/scan/tag-rules` for each
  matched card's rule tags and pre-fills them, marked, for review; a removed rule tag stays off that card.
  It re-checks a card when its identity, finish, condition, language or the game changes, and holds the
  commit until every card is checked. *Apply tag rules* in the action bar turns this off (per browser).
  The commit itself doesn't re-apply rules (`ScanCommitRequest.ApplyTagRules` is false), so what was
  reviewed is what's saved.
- **Imports** (CSV, deck URL, location import, list *Add new*, location *Add card*, binder pocket add
  from the catalog) apply the enabled rules to the new lots right after they're saved
  (`TagRuleService.ApplyToNewLots`) and report `ruleTagged`. A rule failure is logged, never fails the
  import.
- **Run now** applies one rule to every owned card it matches, across every site, after a preview
  (`POST /api/tag-rules/preview`).
- **Validation is strict** (`TagRuleQueryValidator`): unknown fields and `is:` flags are errors (the
  search box would treat them as a name search), and rules can't use `loc:`, `tag:`, prices or `date:`.
- Storage: `TagRules` table (migration `AddTagRules`; tags as a JSON array of names, so a rule can name
  a tag that doesn't exist yet). API (`TagRulesController`, admin only): `GET/POST /api/tag-rules`,
  `PUT/DELETE /api/tag-rules/{id}`, `POST /api/tag-rules/validate`, `POST /api/tag-rules/preview`,
  `POST /api/tag-rules/{id}/run`.

## Scan validation

The Scan page (and batches/audits) has a **Validation rule** box: a search query every scanned card should
match. **Validate** checks the whole list (not just the filtered view) with `POST /api/scan/validate`;
cards that don't match — and scans with no identity — get a red frame plus a *Failed validation* chip, and
a *N failed* button filters the list to them. Each run clears the previous results first; nothing re-checks
automatically. Queries use the strict `TagRuleQueryValidator` with `tag:` allowed (no `loc:`, prices or
`date:`), and are matched in memory like tag rules on unsaved scans (`ScanValidationService`).

Saved rules are per game and shared: `ScanValidationRules` table (migration `AddScanValidationRules`,
unique by game + name; saving an existing name replaces it). API (`ScanValidationController`):
`GET /api/scan/validation-rules?game=` and `POST /api/scan/validate` need `scan.view`;
`POST /api/scan/validation-rules` and `DELETE /api/scan/validation-rules/{id}` need `scan.commit`.

Related: collection search now resolves every MTG catalog field (`kw:`, `pow`, `f:`, catalog `is:` flags…)
against the printings you own (`IGameFieldResolver.ResolveFieldCardIds(…, candidateIds)`), and an unknown
`is:` flag matches nothing instead of everything. The matching ids travel as one JSON parameter
(`EF.Parameter`). A catalog field under `or`/negation is evaluated in memory instead
(`CollectionQueryBuilder.ApplyInMemory`): SQL Server re-reads the JSON list per row there, which timed out
on a ~50k-card collection; in memory it takes ~2 s.

## Order CSV import

**Sales → Orders → Import CSV** creates orders from a CSV using a reusable column-mapping **template**.
The built-in **TCGPlayer Shipping Export** template ships pre-mapped to that file's columns and is the
default. You can adjust the mapping per import and **Save as template** to store a custom layout for a
different marketplace's export.

Flow: upload the CSV → the dialog reads its headers → map each order field to a column (Order Number is
required; it's the dedup key) → preview the resolved rows (new vs. matched customer, duplicate orders
already imported) → import the selected rows. Import is **idempotent**: rows whose order number already
exists are skipped, and customers are matched/reused by name + postal code.

Imported orders are **header-only**: they capture the customer, channel, order number/date, shipping fee,
tracking/carrier, and the aggregate item count + product value (`ImportedItemCount` / `ImportedProductValue`).
They are **not** backed by inventory lots, so importing does not decrement stock.

- API: `POST /api/orders/import/{headers,preview,commit}`, template CRUD under
  `/api/orders/import/templates`, field list at `GET /api/orders/import/fields`.
- Custom templates persist to `<DataDirectory>/order-import-templates.json`; built-ins are merged in at
  read time and can't be edited or deleted.

## Ship by scanning labels

The **Ship** page (`/ship`) marks orders shipped by scanning each package's shipping-label barcode (phone
camera, desk webcam, or a keyboard-wedge handheld scanner typing into the tracking box). With **Auto-ship**
on, a single open (Created/Packed) match ships immediately; otherwise each waits for **Mark shipped**.
Shipping goes through the same `OrderService.SetStatusAsync` as a kanban drag, into the first lane whose
behavior is Shipped. The order drawer's **Scan label** button fills **Tracking** the same way.

- Matching (`TrackingNumbers` in `OmniCard.Shared/Sales`) compares normalized values (upper-case
  alphanumerics) by equality or suffix (≥ 10 chars), so USPS IMpb barcodes (`420` + ZIP + tracking) and
  FedEx's 22/34-digit barcodes match the bare tracking number. Done in memory over non-cancelled orders.
- API: `POST /api/orders/ship-scan` `{ code, ship }` → `{ outcome, tracking, orders }`
  (`shipped`/`ready`/`alreadyShipped`/`ambiguous`/`notFound`); `POST /api/orders/{id}/ship`.
- Permission: **Sales · Orders ▸ Ship (scan labels)** (`sales.orders.ship`) — or **Edit**, which also grants it.
- **The live camera needs HTTPS** (browsers only allow `getUserMedia` on a secure origin or `localhost`).
  Over plain HTTP the page says so and falls back to typed / handheld-scanner entry. Barcodes are decoded
  in the browser with ZXing (`@zxing/browser`); nothing is uploaded.

## Customer kiosk

A **customer-facing kiosk** at `/kiosk`: customers type a name, browse/search the cards in admin-chosen
locations, and put copies on a pick list that staff fulfill from **Sales ▸ Kiosk**. It is a second page of
the same Vite build (`ClientApp/kiosk.html` → `wwwroot/app/kiosk.html`, `src/kiosk/`), served by
`MapFallbackToFile("/kiosk…")` with no staff shell, router or sign-in.

- **Devices.** An administrator opens `/kiosk` on the device and enters their credentials once
  (`POST /api/kiosk/enroll`). The device gets its own cookie scheme (`KioskAuth`, cookie `OmniCard.Kiosk`,
  path `/api/kiosk`, 1-year sliding) naming a `KioskDevice` row — never the staff cookie, so the device
  can't reach `/app` or any staff API. Revoke in **Administration ▸ Kiosk**, or tap the kiosk logo five
  times and enter admin credentials (`/api/kiosk/unenroll`).
- **Holds.** Cards are grouped by printing + condition + language; adding one creates `KioskHold` rows on
  specific lots. Available = lot quantity − other open sessions' holds − copies on open (Created/Packed)
  orders − copies actively listed on TCGplayer/eBay (in-store Manual listings stay sellable). All writes go
  through the singleton `KioskService` under one lock, so two kiosks can't hold the same copy (single
  server process). The order editor's sellable search/AddLine also subtracts kiosk holds.
- **Sessions.** One customer per device; the name is unique among open sessions (`ActiveNameKey`, filtered
  unique index). A Browsing session idle past the timeout expires lazily (holds ignored immediately,
  closed on the next write/queue read); Queued/Picking lists never expire.
- **Fulfillment.** `POST /api/kiosk-queue/{id}/order` creates a Manual order (status Created) for a new or
  existing customer, one line per held lot at the price shown to the customer, then closes the list —
  the open order keeps the copies reserved until it ships.
- **Settings** (`kiosk-settings.json`, admin only, `GET|PUT /api/kiosk-settings`): open/closed, browsable
  location ids, idle timeout (2–240 min), show prices, welcome message. Devices:
  `GET /api/kiosk-settings/devices`, `POST …/devices/{id}/revoke`, `DELETE …/devices/{id}`.
- Tables `KioskDevices`, `KioskSessions`, `KioskHolds` (migration `AddKiosk`). Permissions: **Sales ·
  Kiosk ▸ View pick-list queue** (`sales.kiosk.view`) and **Fulfill pick lists** (`sales.kiosk.fulfill`).
  Not site-scoped (like the rest of Sales): the browsable locations are the admin's explicit choice.
- In dev, open the kiosk at http://localhost:5173/app/kiosk.html.

## Card labels

**Administration ▸ Labels** holds shared, user-designed **label templates** for card-holder labels; the app
defines no layout of its own. A template (`LabelTemplate` in `OmniCardDbContext`, migration
`AddLabelTemplates`) is the sheet the user typed in — page size, margins, columns × rows and the spacing
between them, all in millimetres (the editor shows inches or mm) — plus the design of one label. The label
size is derived from the sheet (`LabelTemplate.LabelWidth/LabelHeight`).

- **Designer and PDFs are client-side, built on [pdfme](https://pdfme.com) (MIT).** The design is a pdfme
  template JSON for one blank label page; the SPA's Label Designer embeds pdfme's WYSIWYG designer, and a
  print run renders one pdfme page per distinct label and tiles them onto sheets with pdfme's pdf-lib
  (`ClientApp/src/lib/labels/`). Barcodes (QR, Code 128/39, UPC-A/E, EAN-13/8, ITF-14, GS1 Data Matrix,
  PDF417) come from bwip-js via pdfme. Both pdfme chunks load on demand, so the main bundle is unaffected.
  `vite.config.ts` aliases `@pdfme/converter` to a stub: the designer only needs it for uploaded background
  PDFs, which labels never use, and the real one adds ~20 MB of wasm/worker to the publish.
- **Card fields:** an element named after a field (`cardName`, `setCode`, `collectorNumber`, `marketPrice`,
  `cardUrl`, `tcgplayerUrl`, `cardId`, … — see `lib/labels/fields.ts`) is filled from the card; "Text with
  fields" elements combine them (`{setCode} #{collectorNumber}`).
- **Card pages:** the `cardUrl` field links to `/app/card/{lotId}`, a read-only card page (with the editor for
  users who may edit). QR codes use the address in the print dialog's **Card links open** box — by default the
  address the browser is using, remembered per browser — so print from (or type) an address phones on the
  LAN can reach, not `localhost`.
- API: `GET|POST /api/labels/templates`, `GET|PUT|DELETE /api/labels/templates/{id}`; `POST /api/labels/data`
  `{ lotIds }` or `{ containerId, game, q }` → the label values per card (site-scoped; max 5,000 cards).
- Permissions: **Labels ▸ Print labels** (`labels.print`) and **Labels ▸ Manage label templates**
  (`labels.edit`). Templates are configuration and survive **Reset collection**.

## Reset collection (admin)

**Administration ▸ Reset Collection** wipes the owned collection to start over while keeping all sales data
(`CollectionResetService`, `GET`/`POST /api/collection-reset`, admin-only; `POST` needs
`{ "confirmation": "RESET COLLECTION" }`). It runs in one transaction with set-based deletes:

- **Removed:** every inventory lot (singles and sealed), storage location, non-default site (+ grants), card
  list, trade/trade session, tag, tag rule, and location saved views. Also removed: movements of lots that never
  sold, and products nothing references any more.
- **Kept:** orders, order lines, edits, customers, listings (all statuses), and the Acquire/Sell movements and
  products of every lot that ever sold (realized profit is computed from them). Lots still for sale (open
  Listed/Picked listing, or any eBay listing record) are kept with their tags, location, return-to location
  and that location's site, as are Bulk, the for-sale location and scan-folder / open-batch default
  locations. Users, roles, settings, deck types, scan batches and the per-game catalog DBs are untouched.
- **No files are deleted** (scan images, trade photos, card art), so restoring a database backup fully
  reverts a reset. Back up first:

```sql
BACKUP DATABASE [OmniCard] TO DISK = N'D:\Backups\OmniCard-pre-reset.bak' WITH COPY_ONLY, INIT, CHECKSUM;
RESTORE VERIFYONLY FROM DISK = N'D:\Backups\OmniCard-pre-reset.bak' WITH CHECKSUM;
```

To revert, stop the site (IIS app pool / `OmniCard.Web.exe`) so nothing holds a connection, then:

```sql
USE master;
ALTER DATABASE [OmniCard] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
RESTORE DATABASE [OmniCard] FROM DISK = N'D:\Backups\OmniCard-pre-reset.bak' WITH REPLACE;
ALTER DATABASE [OmniCard] SET MULTI_USER;
```

## MCP server (Claude, Gemini, etc.)

OmniCard hosts an in-process **Model Context Protocol** server so MCP-capable apps (Claude Desktop,
Claude Code, Gemini CLI, …) can query the collection directly. It's mounted at **`/mcp`** over the
Streamable-HTTP transport, reusing the same services and read paths as the SPA API.

**Scope:** read-only. Tools available:
- `search_collection` (Scryfall-style syntax), `get_card`, `list_locations`, `top_value_cards`,
  `collection_dashboard`
- `list_sites` — sites are major physical locations holding many storage locations; pass a site id or
  name as `site` to `search_collection` / `list_locations` to scope to one place. MCP calls carry no
  OmniCard user identity, so they see **every** site (per-user site visibility applies to the SPA/API).
- `list_inventory_products`, `list_inventory_lots`, `inventory_valuation`
- `list_orders`, `get_order`, `list_customers`
- `lookup_card_catalog`, `set_checklist`, `card_price`

### Security — two modes

The `/mcp` endpoint runs in one of two modes depending on config:

1. **Loopback only (default, no OAuth configured).** Reachable only from the machine the server runs
   on (any non-loopback request gets `403`), with no authentication. Good for local use. To allow
   remote clients *without* OAuth (only if you put your own auth in front), set `Mcp:AllowRemote: true`
   — otherwise leave it `false`.
2. **OAuth resource server (for web/remote access).** When an external identity provider is
   configured, `/mcp` requires a valid **JWT access token** from that IdP and the loopback restriction
   is lifted. OmniCard only *validates* tokens (issuer, audience, signature via the IdP's JWKS) — it
   never issues them. This is the mode to use behind your public HTTPS domain.

### OAuth configuration

Add to the shared `appsettings.json` (`%LocalAppData%\OmniCard\appsettings.json`):

```jsonc
"Mcp": {
  "PublicBaseUrl": "https://omnicard.example.com",       // your public HTTPS origin
  "OAuth": {
    "Enabled": true,
    "Authority": "https://login.example.com/...",         // IdP issuer (OIDC discovery finds the JWKS)
    "Audience": "api://omnicard-mcp",                      // must equal the audience the IdP mints
    "Scopes": [ "mcp:tools" ]
  }
}
```

OAuth activates only when `Enabled` **and** `Authority`, `Audience`, and `PublicBaseUrl` are all set;
otherwise the server stays in loopback mode. Clients discover the IdP automatically via the
**Protected Resource Metadata** document served at `/.well-known/oauth-protected-resource`.

**Per-IdP setup** (set `Authority`/`Audience` to match):

- **Microsoft Entra ID:** register an app, **Expose an API** → add a scope (e.g. `mcp:tools`) and note
  the Application ID URI. `Authority = https://login.microsoftonline.com/<tenant-id>/v2.0`,
  `Audience = <Application ID URI>` (e.g. `api://<client-id>`).
- **Auth0:** create an **API** with an identifier and a permission/scope.
  `Authority = https://<your-tenant>.auth0.com/`, `Audience = <API identifier>`.
- **Keycloak:** a realm + client with an audience mapper.
  `Authority = https://<host>/realms/<realm>`, `Audience = <client/audience id>`.

**HTTPS / IIS:** OAuth requires HTTPS. In-process IIS hosting sees the public host/scheme, so the PRM
document and challenge URLs come out correct. If you host **out-of-process** behind a reverse proxy,
enable forwarded headers so the request scheme/host reflect the public URL.

### Connecting a client

**Loopback mode** (on the server host):
- **Claude Code:** `claude mcp add --transport http omnicard http://localhost:5000/mcp`
- **Claude Desktop** (`claude_desktop_config.json`): `"omnicard": { "type": "http", "url": "http://localhost:5000/mcp" }`
- **Gemini CLI** (`~/.gemini/settings.json`): `"omnicard": { "httpUrl": "http://localhost:5000/mcp" }`

**OAuth mode** (from anywhere): point the same client at your public URL, e.g.
`claude mcp add --transport http omnicard https://omnicard.example.com/mcp`. On first connect the
client discovers the IdP and runs the OAuth login in a browser; no token is entered by hand.

Claude.ai *web* custom connectors additionally require Dynamic Client Registration, which this phase
doesn't implement — it targets remote Claude Desktop / Claude Code / Gemini CLI.

## Localization

The SPA is internationalized with **react-i18next** + **i18next-browser-languagedetector**. It picks
the visitor's browser culture automatically and falls back to **en-US** (the only language bundle that
ships today, and the source of truth). A `?lng=<culture>` query-string override is available for
testing (e.g. `http://localhost:5173/?lng=de-DE`).

Value formatting is locale-aware even when the text falls back to en-US: numbers, dates, and currency
are formatted for the visitor's actual culture via the `Intl` APIs (`ClientApp/src/i18n/format.ts`,
`useFormatters()`). Currency amounts stay denominated in **USD** — only their *presentation* (decimal
/ grouping separators, symbol placement) is localized, so a value is never reinterpreted as a
different currency.

- **Init & detection:** `ClientApp/src/i18n/index.ts` (single `translation` bundle, `fallbackLng:
  'en-US'`, detection order query-string → `navigator` → `<html lang>`). Imported once from
  `src/main.tsx`.
- **Strings:** `ClientApp/src/i18n/locales/en-US/*.json`, one file per feature area (namespace):
  `common` (shared actions/labels/conditions/channels/statuses), `nav`, `auth`, `account`, `dashboard`,
  `collection`, `locations`, `binder`, `sets`, `scan`, `sales`, `inventory`, `importing`, `lists`,
  `trades`, `settings`, `deckbox`, `dialogs`, `search`, `help`, `labels`, `kiosk`. Each file is keyed by its namespace object
  and spread into the bundle in `index.ts`.
- **In-app help:** the **Help** item pinned to the bottom of the sidebar opens `/help`. Topics are
  Markdown files, one per topic per culture, in `ClientApp/src/help/<culture>/<topic-id>.md`; each topic
  falls back to en-US on its own. Screenshots in `ClientApp/src/help/images/` are shared by every
  culture. `ClientApp/src/help/content.ts` sets the topic order and grouping; `help/HelpMarkdown.tsx`
  renders the Markdown subset the topics use: headings, lists, bold/italic/code, links
  (`help:<topic>#anchor`, `/route`), images, `> [!TIP]`/`[!NOTE]`/`[!WARNING]` callouts and pipe tables.

### Adding a language

1. Copy `ClientApp/src/i18n/locales/en-US/` to `ClientApp/src/i18n/locales/<culture>/` (e.g. `de-DE`)
   and translate the string values (keep the keys and `{{placeholders}}` unchanged).
2. Import the new files in `ClientApp/src/i18n/index.ts` and add them under
   `resources['<culture>'].translation`.
3. Optionally translate the help: copy `ClientApp/src/help/en-US/` to `ClientApp/src/help/<culture>/`
   and translate the topics. No registration is needed; untranslated topics stay in English.
4. Rebuild the SPA. No component changes are needed — browsers set to that culture pick it up
   automatically, and any key you leave untranslated falls back to en-US.

### Conventions (when adding or changing UI)

- Never hard-code user-facing text. Use `const { t } = useTranslation();` and `t('<namespace>.<key>')`,
  reusing `common.*` for generic terms rather than duplicating them.
- Format every number/currency/date through `useFormatters()` (`fmt.money` / `fmt.number` /
  `fmt.percent` / `fmt.date` / `fmt.dateTime`) — do not call `toLocaleString`/`toFixed`/`new Date().toLocale*`
  directly for display. Prefer a DTO's numeric field over a server-preformatted string.
- When a change alters what users see or do, update the matching help topic in
  `ClientApp/src/help/en-US/` (and its screenshot if it no longer matches).
- Do **not** translate data returned by the server (card/set/customer names, error messages) or
  identifiers/enum values sent back to the API.

## Tests

Web-facing tests live under `OmniCard.Tests/Web/` (controllers, services). Run the whole suite:

```bash
dotnet test OmniCard.Tests/OmniCard.Tests.csproj
```
