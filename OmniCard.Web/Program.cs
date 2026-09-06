using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Sdb = OmniCard.Web.Data.SqlServerDb;
using OmniCard.CardMatching;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Load shared settings from %localappdata%/OmniCard (same as desktop app)
var sharedSettingsDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "OmniCard");
var sharedAppsettings = Path.Combine(sharedSettingsDir, "appsettings.json");
if (File.Exists(sharedAppsettings))
    builder.Configuration.AddJsonFile(sharedAppsettings, optional: true, reloadOnChange: true);

// --db command-line argument overrides config; fall back to DataPathService's resolved directory
var dataDir = builder.Configuration.GetValue<string>("db")
    ?? builder.Configuration.GetValue<string>("DataDirectory")
    ?? new DataPathService(sharedSettingsDir).DataDirectory;

if (string.IsNullOrWhiteSpace(dataDir))
{
    Console.Error.WriteLine("Error: DataDirectory not configured. Use --db <path> or set DataDirectory in appsettings.json.");
    return 1;
}

// The unified store (collection / inventory / sales) now lives in SQL Server for multi-user
// concurrency; the per-game catalog DBs below stay on SQLite (read-mostly reference caches). The
// connection string comes from ConnectionStrings:OmniCard, falling back to the local dev default.
var connectionString = OmniCard.Web.Data.SqlServerDb.ConnectionString(builder.Configuration);

var scansDir = Path.Combine(dataDir, "scans");

builder.Services.AddSignalR();
builder.Services.AddControllers(options =>
    options.Filters.Add<OmniCard.Web.Api.ConcurrencyExceptionFilter>());
builder.Services.AddHttpClient();
builder.Services.AddOpenApi();

// Everything is on SQL Server now — the unified store AND the six per-game catalog DBs (one DB per
// game, e.g. OmniCard_Scryfall). The catalogs used to be SQLite files owned/refreshed by the desktop;
// they now live in SQL Server so the web owns them end-to-end (CatalogController refreshes them and
// the desktop can be retired). Catalog schemas are EnsureCreated at startup (they're disposable
// reference caches — refresh wipes + reloads — so no migrations). One-time data copy from the old
// SQLite files: OmniCard.DbMigrator.
builder.Services.AddDbContextFactory<OmniCardDbContext>(options =>
    Sdb.Configure(options, connectionString));
builder.Services.AddDbContextFactory<ScryfallDbContext>(options =>
    Sdb.ConfigureCatalog(options, Sdb.CatalogConnectionString(builder.Configuration, "Scryfall")));
builder.Services.AddDbContextFactory<OptcgDbContext>(options =>
    Sdb.ConfigureCatalog(options, Sdb.CatalogConnectionString(builder.Configuration, "Optcg")));
builder.Services.AddDbContextFactory<RiftboundDbContext>(options =>
    Sdb.ConfigureCatalog(options, Sdb.CatalogConnectionString(builder.Configuration, "Riftbound")));
builder.Services.AddDbContextFactory<PokemonDbContext>(options =>
    Sdb.ConfigureCatalog(options, Sdb.CatalogConnectionString(builder.Configuration, "Pokemon")));
builder.Services.AddDbContextFactory<YugiohDbContext>(options =>
    Sdb.ConfigureCatalog(options, Sdb.CatalogConnectionString(builder.Configuration, "Yugioh")));
builder.Services.AddDbContextFactory<FinalFantasyDbContext>(options =>
    Sdb.ConfigureCatalog(options, Sdb.CatalogConnectionString(builder.Configuration, "FinalFantasy")));

// Infrastructure services needed by game services
builder.Services.AddSingleton<IDataPathService>(new WebDataPathService(dataDir));
builder.Services.AddSingleton<IPerceptualHashService, OmniCard.Imaging.PerceptualHashService>();
builder.Services.AddSingleton<IOcrMatchingService, OmniCard.Imaging.OcrMatchingService>();
builder.Services.AddSingleton<SetSymbolCache>();
builder.Services.Configure<ScryfallSettings>(builder.Configuration.GetSection("Scryfall"));

// Game services
builder.Services.AddSingleton<ScryfallService>();
builder.Services.AddSingleton<ICardGameService>(sp => sp.GetRequiredService<ScryfallService>());
builder.Services.AddSingleton<OptcgService>();
builder.Services.AddSingleton<ICardGameService>(sp => sp.GetRequiredService<OptcgService>());
builder.Services.AddSingleton<RiftboundService>();
builder.Services.AddSingleton<ICardGameService>(sp => sp.GetRequiredService<RiftboundService>());
builder.Services.AddSingleton<PokemonService>();
builder.Services.AddSingleton<ICardGameService>(sp => sp.GetRequiredService<PokemonService>());
builder.Services.AddSingleton<YugiohService>();
builder.Services.AddSingleton<ICardGameService>(sp => sp.GetRequiredService<YugiohService>());
builder.Services.AddSingleton<FinalFantasyService>();
builder.Services.AddSingleton<ICardGameService>(sp => sp.GetRequiredService<FinalFantasyService>());

// Card & decklist services
builder.Services.AddSingleton<ICardService, WebCardService>();
builder.Services.AddSingleton<WebScanMatchingService>();
builder.Services.AddSingleton<CardImageCacheService>();
builder.Services.AddSingleton<CatalogRefreshService>();
builder.Services.AddSingleton<IDecklistService, DecklistService>();
builder.Services.AddSingleton<ITradeService, TradeService>();
// Applies finalized trade drafts (single-card + multi-card sessions) written by the SPA's trade
// builder to the collection. On the desktop this ran once at launch; with the desktop retired the web
// app now owns application — TradeSessionController calls it on finalize (and it runs once at startup
// below to catch any drafts finalized while the app was down). Writes go through the DI OmniCardDbContext
// factory, which is read-write on SQL Server.
builder.Services.AddSingleton<ITradeImportService, TradeImportService>();
builder.Services.AddSingleton<IListService, ListService>();

// Read-only reporting/query services backing the SPA API (they read via the Mode=ReadOnly
// OmniCardDbContext factory registered above).
builder.Services.AddSingleton<IAnalyticsService, AnalyticsService>();
builder.Services.AddSingleton<ICollectionQueryService, CollectionQueryService>();
builder.Services.AddSingleton<ISetChecklistService, SetChecklistService>();

// Sales & inventory services. On SQL Server the DI-registered OmniCardDbContext factory is
// read-write, so these write straight through it.
//
// eBay (Phase 5b): the entire desktop eBay stack is reused server-side — the only desktop-specific
// piece was token storage (Windows Credential Manager), replaced here by WebCredentialStore (a
// DataProtection-encrypted file). All services degrade gracefully when unconfigured/unconnected
// (GetAccessTokenAsync returns null → listing/end operations return false), so OrderService's
// best-effort ship-time listing-end keeps working without a live eBay connection. Connect via the
// OAuth flow in EbayController. Requires the "eBay" config section (AppId/CertId/RuName/AcceptUrl/
// Environment) — until it's filled in, GetMissingConfiguration() reports what's missing.
builder.Services.Configure<EbaySettings>(builder.Configuration.GetSection("eBay"));
builder.Services.AddSingleton<ICredentialStore, WebCredentialStore>();
builder.Services.AddSingleton<IEbayAuthService, OmniCard.eBay.EbayAuthService>();
builder.Services.AddSingleton<IEbaySellingSettingsService, EbaySellingSettingsService>();
builder.Services.AddSingleton<IEbayCatalogService, OmniCard.eBay.EbayCatalogService>();
builder.Services.AddSingleton<IEbaySellerSetupService, OmniCard.eBay.EbaySellerSetupService>();
builder.Services.AddSingleton<IEbaySyncService, OmniCard.eBay.EbaySyncService>();
builder.Services.AddSingleton<IEbayListingService, OmniCard.eBay.EbayListingService>();
builder.Services.AddSingleton<IInventoryService, InventoryService>();
builder.Services.AddSingleton<ICustomerService, CustomerService>();
builder.Services.AddSingleton<IOrderService, OrderService>();

// Import/export + PDF generation (QuestPDF exporters set their own Community license internally).
builder.Services.AddSingleton<ICsvExportImportService, CsvExportImportService>();
builder.Services.AddSingleton<IReceiptService, ReceiptService>();
builder.Services.AddSingleton<IReceiptPdfExporter, OmniCard.Audit.ReceiptPdfExporter>();
builder.Services.AddSingleton<ISetChecklistPdfExporter, OmniCard.Audit.SetChecklistPdfExporter>();
builder.Services.AddSingleton<IPriceSheetService, PriceSheetService>();
builder.Services.AddSingleton<IPriceSheetPdfExporter, OmniCard.Audit.PriceSheetPdfExporter>();
builder.Services.AddSingleton<IPickListPdfExporter, OmniCard.Audit.PickListPdfExporter>();

// --- Binder editor: the one deliberate WRITE surface in the otherwise read-only web app ---
// A single writable factory against inventory.db, injected only into the binder-edit services so the
// read-only invariant holds everywhere else. Editing is gated by a passphrase (Binder:EditPassphrase)
// held in the session.
var writableFactory = new WritableOmniCardDbContextFactory(connectionString);
builder.Services.AddSingleton(writableFactory);
builder.Services.AddSingleton<IStorageContainerService>(_ => new StorageContainerService(writableFactory));
builder.Services.AddSingleton<ITagService>(_ => new TagService(writableFactory));
builder.Services.AddSingleton<ISalesSettingsService, SalesSettingsService>();
builder.Services.AddSingleton<IListingService>(sp =>
    new ListingService(writableFactory, sp.GetRequiredService<ISalesSettingsService>()));
builder.Services.AddSingleton(sp =>
    new WebBinderCardService(writableFactory, sp.GetRequiredService<IDataPathService>()));
builder.Services.AddScoped<BinderStateBuilder>();

// Persist DataProtection keys to the data dir so WebCredentialStore's encrypted eBay tokens survive
// app-pool recycles and don't depend on the IIS identity having a roaming profile.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "dataprotection-keys")));

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromHours(8);
});

var app = builder.Build();

// Apply any pending SQL Server migrations to the unified store on startup (creates the DB on first
// run). The catalog SQLite DBs are unaffected.
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OmniCardDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.Migrate();
}

// Ensure the per-game catalog SQL Server databases + schemas exist (one DB per game). EnsureCreated
// creates the DB and tables on first run and is a no-op once they exist; catalogs are disposable
// reference caches so they use EnsureCreated rather than migrations. Data is copied in from the old
// SQLite files by OmniCard.DbMigrator, and refreshed in-place by CatalogController.
using (var scope = app.Services.CreateScope())
{
    void EnsureCatalog<TContext>() where TContext : DbContext
    {
        try
        {
            using var ctx = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>().CreateDbContext();
            ctx.Database.EnsureCreated();
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Failed to ensure catalog schema for {Context}", typeof(TContext).Name);
        }
    }
    EnsureCatalog<ScryfallDbContext>();
    EnsureCatalog<OptcgDbContext>();
    EnsureCatalog<RiftboundDbContext>();
    EnsureCatalog<PokemonDbContext>();
    EnsureCatalog<YugiohDbContext>();
    EnsureCatalog<FinalFantasyDbContext>();
}

// Apply any finalized-but-unapplied trade drafts left in the shared trades folder (e.g. a session
// finalized just before the app was stopped). Idempotent — already-applied drafts are skipped.
using (var scope = app.Services.CreateScope())
{
    try
    {
        var applied = scope.ServiceProvider.GetRequiredService<ITradeImportService>().ImportPendingTrades();
        if (applied > 0)
            app.Logger.LogInformation("Applied {Count} pending trade draft(s) at startup.", applied);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Failed to apply pending trades at startup.");
    }
}

app.UseStaticFiles();
app.UseSession();

// Serve scan images from the data directory
if (Directory.Exists(scansDir))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(scansDir),
        RequestPath = "/scans"
    });
}

// Serve locally-cached card artwork (populated by CardImageCacheService / the catalog "images"
// refresh) so the SPA can self-host images instead of hot-linking CDNs.
{
    var cardImagesDir = Path.Combine(dataDir, "card-images");
    Directory.CreateDirectory(cardImagesDir);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(cardImagesDir),
        RequestPath = CardImageCacheService.RequestPath,
    });
}

app.MapControllers();
app.MapHub<OmniCard.Web.Hubs.ScanHub>("/hubs/scan");

// OpenAPI document at /openapi/v1.json — consumed by the SPA's typed client generator.
app.MapOpenApi();

// The React SPA (built into wwwroot/app by `npm run build`) is the entire app now — the legacy Razor
// pages have been retired. Serve it at /app, with a fallback so client-side deep links (e.g.
// /app/collection) resolve to its index.html, and redirect the site root to it.
app.MapFallbackToFile("/app/{*path:nonfile}", "/app/index.html");
app.MapGet("/", () => Results.Redirect("/app/"));

Console.WriteLine($"Serving collection from: {dataDir}");
app.Run();
return 0;
