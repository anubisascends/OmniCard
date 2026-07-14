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

var dbPath = Path.Combine(dataDir, "collection.db");
if (!File.Exists(dbPath))
{
    Console.Error.WriteLine($"Error: Database not found at {dbPath}");
    return 1;
}

var scansDir = Path.Combine(dataDir, "scans");

builder.Services.AddRazorPages();
builder.Services.AddSignalR();
builder.Services.AddControllers();
builder.Services.AddHttpClient();

// Database contexts
builder.Services.AddDbContextFactory<CollectionDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath};Mode=ReadOnly"));
builder.Services.AddDbContextFactory<ScryfallDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(dataDir, "scryfall.db")};Mode=ReadOnly"));
builder.Services.AddDbContextFactory<OptcgDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(dataDir, "optcg.db")};Mode=ReadOnly"));

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

// Card & decklist services
builder.Services.AddSingleton<ICardService, WebCardService>();
builder.Services.AddSingleton<IDecklistService, DecklistService>();

var app = builder.Build();

app.UseStaticFiles();

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

Console.WriteLine($"Serving collection from: {dataDir}");
app.Run();
return 0;
