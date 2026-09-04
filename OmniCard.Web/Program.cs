using Microsoft.EntityFrameworkCore;
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

builder.Services.AddRazorPages();
builder.Services.AddSignalR();
builder.Services.AddControllers(options =>
    options.Filters.Add<OmniCard.Web.Api.ConcurrencyExceptionFilter>());
builder.Services.AddHttpClient();
builder.Services.AddOpenApi();

// Unified store on SQL Server; catalog DBs on SQLite (read-only).
builder.Services.AddDbContextFactory<OmniCardDbContext>(options =>
    OmniCard.Web.Data.SqlServerDb.Configure(options, connectionString));
builder.Services.AddDbContextFactory<ScryfallDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(dataDir, "scryfall.db")};Mode=ReadOnly"));
builder.Services.AddDbContextFactory<OptcgDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(dataDir, "optcg.db")};Mode=ReadOnly"));
builder.Services.AddDbContextFactory<RiftboundDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(dataDir, "riftbound.db")};Mode=ReadOnly"));
builder.Services.AddDbContextFactory<PokemonDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(dataDir, "pokemon.db")};Mode=ReadOnly"));
builder.Services.AddDbContextFactory<YugiohDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(dataDir, "yugioh.db")};Mode=ReadOnly"));
builder.Services.AddDbContextFactory<FinalFantasyDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(dataDir, "fftcg.db")};Mode=ReadOnly"));

// Infrastructure services needed by game services
builder.Services.AddSingleton<IDataPathService>(new WebDataPathService(dataDir));
builder.Services.AddSingleton<IPerceptualHashService, OmniCard.Imaging.PerceptualHashService>();
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
builder.Services.AddSingleton<IDecklistService, DecklistService>();

// Read-only reporting/query services backing the SPA API (they read via the Mode=ReadOnly
// OmniCardDbContext factory registered above).
builder.Services.AddSingleton<IAnalyticsService, AnalyticsService>();
builder.Services.AddSingleton<ICollectionQueryService, CollectionQueryService>();
builder.Services.AddSingleton<ISetChecklistService, SetChecklistService>();

// Sales & inventory services. On SQL Server the DI-registered OmniCardDbContext factory is
// read-write, so these write straight through it. eBay is stubbed until Phase 5.
builder.Services.AddSingleton<IEbayListingService, NoOpEbayListingService>();
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

app.MapRazorPages();
app.MapControllers();
app.MapHub<OmniCard.Web.Hubs.ScanHub>("/hubs/scan");

// OpenAPI document at /openapi/v1.json — consumed by the SPA's typed client generator.
app.MapOpenApi();

// Serve the React SPA (built into wwwroot/app by `npm run build`) at /app, with a fallback so
// client-side deep links (e.g. /app/collection) resolve to its index.html. The legacy Razor pages
// stay at the root during the migration; the SPA is additive under /app for now.
app.MapFallbackToFile("/app/{*path:nonfile}", "/app/index.html");

Console.WriteLine($"Serving collection from: {dataDir}");
app.Run();
return 0;
