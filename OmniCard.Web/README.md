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
- **Per-user accounts + granular permissions** — username + password sign-in (passwords stored as
  salted PBKDF2 hashes). A built-in `Admin` account (default password `admin`) is seeded on first
  run — change it after signing in. "Remember me" issues a persistent, encrypted auth cookie.
  Manage accounts under **Administration → Users** and permission bundles under
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
built `wwwroot/app` output does.

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
   - **read/write** on the data directory (`scans/`, `card-images/`, `dataprotection-keys/`),
   - access to SQL Server (or use a SQL login in the connection string instead of Windows auth).

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
| One Piece | en, ja, fr | poneglyph `/v1/search?lang=` | Rows keyed `OP01-001@ja`; names/colours kept English, art is the language's own; English price fallback |
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
  create/rename/delete locations in it, commit lists into it).
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
  `common` (shared actions/labels/conditions/channels/statuses), `nav`, `auth`, `dashboard`,
  `collection`, `locations`, `binder`, `sets`, `scan`, `sales`, `inventory`, `importing`, `lists`,
  `trades`, `settings`, `deckbox`, `dialogs`, `search`. Each file is keyed by its namespace object and
  spread into the bundle in `index.ts`.

### Adding a language

1. Copy `ClientApp/src/i18n/locales/en-US/` to `ClientApp/src/i18n/locales/<culture>/` (e.g. `de-DE`)
   and translate the string values (keep the keys and `{{placeholders}}` unchanged).
2. Import the new files in `ClientApp/src/i18n/index.ts` and add them under
   `resources['<culture>'].translation`.
3. Rebuild the SPA. No component changes are needed — browsers set to that culture pick it up
   automatically, and any key you leave untranslated falls back to en-US.

### Conventions (when adding or changing UI)

- Never hard-code user-facing text. Use `const { t } = useTranslation();` and `t('<namespace>.<key>')`,
  reusing `common.*` for generic terms rather than duplicating them.
- Format every number/currency/date through `useFormatters()` (`fmt.money` / `fmt.number` /
  `fmt.percent` / `fmt.date` / `fmt.dateTime`) — do not call `toLocaleString`/`toFixed`/`new Date().toLocale*`
  directly for display. Prefer a DTO's numeric field over a server-preformatted string.
- Do **not** translate data returned by the server (card/set/customer names, error messages) or
  identifiers/enum values sent back to the API.

## Tests

Web-facing tests live under `OmniCard.Tests/Web/` (controllers, services). Run the whole suite:

```bash
dotnet test OmniCard.Tests/OmniCard.Tests.csproj
```
