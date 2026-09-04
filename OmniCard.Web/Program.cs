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

var dbPath = Path.Combine(dataDir, "inventory.db");
if (!File.Exists(dbPath))
{
    Console.Error.WriteLine($"Error: Database not found at {dbPath}");
    return 1;
}

// Patch any schema drift on the shared inventory.db — new columns get added to the EF model
// over time, and normally the desktop app's own startup (UnifiedMigrationService) is what
// applies them to the on-disk file. The web app can run for long stretches without the desktop
// ever restarting, so without this it 500s on "no such column" until the desktop happens to
// launch first. This opens its own short-lived, writable connection to the raw file — separate
// from (and unaffected by) the Mode=ReadOnly connections registered below.
using (var schemaLoggerFactory = LoggerFactory.Create(b => b.AddConsole()))
    UnifiedMigrationService.EnsureUnifiedSchema(dataDir, schemaLoggerFactory.CreateLogger("SchemaCheck"));

var scansDir = Path.Combine(dataDir, "scans");

builder.Services.AddRazorPages();
builder.Services.AddSignalR();
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddOpenApi();

// Database contexts
builder.Services.AddDbContextFactory<OmniCardDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath};Mode=ReadOnly"));
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

// --- Binder editor: the one deliberate WRITE surface in the otherwise read-only web app ---
// A single writable factory against inventory.db, injected only into the binder-edit services so the
// read-only invariant holds everywhere else. Editing is gated by a passphrase (Binder:EditPassphrase)
// held in the session.
var writableFactory = new WritableOmniCardDbContextFactory(dbPath);
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

Console.WriteLine($"Serving collection from: {dataDir}");
app.Run();
return 0;
