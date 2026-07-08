using Microsoft.EntityFrameworkCore;
using OmniCard.Data;

var builder = WebApplication.CreateBuilder(args);

// --db command-line argument overrides config
var dataDir = builder.Configuration.GetValue<string>("db")
    ?? builder.Configuration.GetValue<string>("DataDirectory")
    ?? "";

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
builder.Services.AddDbContextFactory<CollectionDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath};Mode=ReadOnly"));

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
