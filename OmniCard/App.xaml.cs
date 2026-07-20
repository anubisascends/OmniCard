using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using OmniCard.Interfaces;
using OmniCard.Models;
using System.IO;
using System.Windows;
using OmniCard.Data;
using OmniCard.Controls;
using OmniCard.Controls.Converters;
using OmniCard.Imaging;
using OmniCard.CardMatching;
using OmniCard.Services;
using OmniCard.Audit;
using OmniCard.Scanner;
using OmniCard.eBay;
using OmniCard.Collection;
using OmniCard.Views.Card;
using OmniCard.Views.CollectionCardEditor;
using OmniCard.Views.Connection;
using OmniCard.Views.CoverArtPicker;
using OmniCard.Views.CsvImport;
using OmniCard.Views.HashPreview;
using OmniCard.Views.Root;
using OmniCard.Views.Splash;
using OmniCard.Views.EbayAuth;
using OmniCard.Views.DataLocation;
using OmniCard.Views.SetFilterBuilder;
using OmniCard.Views.SortFilterBuilder;
using OmniCard.Views.AuditReport;
using OmniCard.Views.StorageManager;
using OmniCard.Views.EbayListing;
using OmniCard.Views.ManualAdd;
using OmniCard.Views.DecklistCheck;

namespace OmniCard;

public partial class App : Application
{
    private static readonly string SettingsDirectory = InitSettingsDirectory();

    private static readonly DataPathService DataPathServiceInstance = new(SettingsDirectory);

    public static IHost Host { get; } = new HostBuilder()
        .ConfigureAppConfiguration((_, config) =>
        {
            config.SetBasePath(SettingsDirectory);
            config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            config.AddUserSecrets<App>();
        })
        .UseSerilog((context, services, loggerConfig) =>
        {
            loggerConfig.ReadFrom.Configuration(context.Configuration);
            loggerConfig.WriteTo.File(
                Path.Combine(DataPathServiceInstance.LogsDirectory, "tcgcardscanner-.log"),
                rollingInterval: Serilog.RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
        })
        .ConfigureServices((_, services) =>
        {
            services.AddHostedService<RootView>();
        })
        .ConfigureServices((context, services) =>
        {
            services.Configure<DisplaySettings>(context.Configuration.GetSection("Display"));
            services.Configure<EbaySettings>(context.Configuration.GetSection("eBay"));
            services.Configure<ScryfallSettings>(context.Configuration.GetSection("Scryfall"));
            services.Configure<WebCompanionSettings>(context.Configuration.GetSection("WebCompanion"));
            services.AddSingleton<CollectionViewModel>();
            services.AddSingleton<Views.Inventory.InventoryViewModel>();
            services.AddSingleton<RootViewModel>();
            services.AddSingleton<ScannerService>();
            services.AddSingleton<WebScannerService>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<IPerceptualHashService, PerceptualHashService>();
            services.AddSingleton<IOcrMatchingService, OcrMatchingService>();
            services.AddSingleton<ICardService, CardService>();
            services.AddSingleton<ICollectionQueryService, CollectionQueryService>();
            services.AddSingleton<IMismatchLogService, MismatchLogService>();
            services.AddSingleton<ScanImageCache>();
            services.AddSingleton<CardArtCache>();
            services.AddHttpClient();

            // Register data path service
            services.AddSingleton<IDataPathService>(DataPathServiceInstance);
            services.AddSingleton<IDataMigrationService, DataMigrationService>();

            // MTG (Scryfall)
            services.AddDbContextFactory<ScryfallDbContext>(options =>
                options.UseSqlite($"Data Source={Path.Combine(DataPathServiceInstance.DataDirectory, "scryfall.db")}"));
            services.AddSingleton<SetSymbolCache>();
            services.AddSingleton<ICardGameService, ScryfallService>();
            services.AddSingleton<IScryfallService>(sp => (ScryfallService)sp.GetRequiredService<IEnumerable<ICardGameService>>().First(s => s.Game == Models.CardGame.Mtg));

            // One Piece (OPTCG)
            services.AddDbContextFactory<OptcgDbContext>(options =>
                options.UseSqlite($"Data Source={Path.Combine(DataPathServiceInstance.DataDirectory, "optcg.db")}"));
            services.AddSingleton<ICardGameService, OptcgService>();
            services.AddSingleton<Services.PriceUpdateService>();

            // Inventory (unified product model) — now the app-wide OmniCardDbContext
            services.AddDbContextFactory<OmniCardDbContext>(options =>
                options.UseSqlite($"Data Source={Path.Combine(DataPathServiceInstance.DataDirectory, "inventory.db")}"));
            services.AddSingleton<IInventoryService, InventoryService>();

            // Storage containers
            services.AddSingleton<IStorageContainerService, StorageContainerService>();

            // Sort/filter presets
            services.AddSingleton<ICollectionPresetService, CollectionPresetService>();

            // CSV export/import
            services.AddSingleton<ICsvExportImportService, CsvExportImportService>();

            // Scan diagnostics
            services.AddSingleton<IDiagnosticExporter, DiagnosticExporter>();
            services.AddSingleton<IScanDiagnosticService, ScanDiagnosticService>();

            // Location audit
            services.AddSingleton<IAuditService, AuditService>();
            services.AddSingleton<IAuditPdfExporter, AuditPdfExporter>();

            // Decklist check
            services.AddSingleton<IDecklistService, DecklistService>();
            services.AddSingleton<IDecklistPdfExporter, DecklistPdfExporter>();

            // eBay OAuth
            services.AddSingleton<ICredentialStore, CredentialStore>();
            services.AddSingleton<IEbayAuthService, EbayAuthService>();
            services.AddSingleton<IEbayCatalogService, EbayCatalogService>();
            services.AddSingleton<IEbayListingService, EbayListingService>();
            services.AddSingleton<IEbaySyncService, EbaySyncService>();

        })
        .ConfigureServices((_, services) =>
        {
            services.AddTransient<ConnectionView>();
            services.AddTransient<ConnectionViewModel>();
            services.AddTransient<CardView>();
            services.AddTransient<CardViewModel>();
            services.AddTransient<HashPreviewView>();
            services.AddTransient<HashPreviewViewModel>();
            services.AddTransient<CollectionCardEditorView>();
            services.AddTransient<CollectionCardEditorViewModel>();
            services.AddTransient<StorageManagerView>();
            services.AddTransient<StorageManagerViewModel>();
            services.AddTransient<EbayAuthView>();
            services.AddTransient<EbayAuthViewModel>();
            services.AddTransient<CsvImportView>();
            services.AddTransient<CsvImportViewModel>();
            services.AddTransient<SortFilterBuilderView>();
            services.AddTransient<SortFilterBuilderViewModel>();
            services.AddTransient<SetFilterBuilderView>();
            services.AddTransient<SetFilterBuilderViewModel>();
            services.AddTransient<DataLocationView>();
            services.AddTransient<DataLocationViewModel>();
            services.AddTransient<CoverArtPickerView>();
            services.AddTransient<CoverArtPickerViewModel>();
            services.AddTransient<OmniCard.Views.MoveToLocation.MoveToLocationView>();
            services.AddTransient<OmniCard.Views.MoveToLocation.MoveToLocationViewModel>();
            services.AddTransient<AuditReportView>();
            services.AddTransient<AuditReportViewModel>();
            services.AddTransient<EbayListingViewModel>();
            services.AddTransient<EbayListingView>();
            services.AddTransient<ManualAddView>();
            services.AddTransient<ManualAddViewModel>();
            services.AddTransient<DecklistCheckView>();
            services.AddTransient<DecklistCheckViewModel>();
            services.AddTransient<Views.Inventory.ProductEditorView>();
            services.AddTransient<Views.Inventory.ProductEditorViewModel>();
            services.AddTransient<Views.Inventory.AddLotView>();
            services.AddTransient<Views.Inventory.AddLotViewModel>();
            services.AddTransient<Views.Inventory.OpenUnitsView>();
            services.AddTransient<Views.Inventory.OpenUnitsViewModel>();
        })
        .Build();

    protected override async void OnStartup(StartupEventArgs e)
    {
        var splash = new SplashWindow();
        splash.Show();

        // Check data directory is reachable
        splash.SetStatus("Checking data directory...");
        var dataDir = DataPathServiceInstance.DataDirectory;
        if (!Directory.Exists(dataDir))
        {
            try
            {
                Directory.CreateDirectory(dataDir);
            }
            catch (Exception ex)
            {
                splash.Close();
                var result = MessageBox.Show(
                    $"Data directory not found or not accessible:\n{dataDir}\n\n{ex.Message}\n\nRevert to default location?",
                    "OmniCard",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    // Delete datapath.json to revert to default
                    var configPath = Path.Combine(AppContext.BaseDirectory, "datapath.json");
                    if (File.Exists(configPath))
                        File.Delete(configPath);

                    MessageBox.Show(
                        "Reverted to default location. Please restart the application.",
                        "OmniCard",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                Shutdown();
                return;
            }
        }

        var loggerFactory = Host.Services.GetRequiredService<ILoggerFactory>();
        var migrationLogger = loggerFactory.CreateLogger("CollectionMigration");

        // Run migrations and initialization on background thread
        await Task.Run(() =>
        {
            splash.SetStatus("Running database migrations...");

            // Ensure HashCorrections table in game databases (added in v0.5)
            EnsureHashCorrectionsInGameDbs(dataDir, migrationLogger);

            // Ensure the unified store's schema is complete on a pre-existing inventory.db
            // (EnsureCreated() below only creates tables/columns for a brand-new database file).
            // collection.db is no longer opened by the app as of Phase 2a Task 6 — its data was
            // migrated into inventory.db's Product/Lot store by an earlier run of this app (see
            // UnifiedMigrationService's MigrationState marker); the file is left on disk for rollback.
            UnifiedMigrationService.EnsureUnifiedSchema(dataDir, migrationLogger);

            splash.SetStatus("Initializing databases...");
            using (var invCtx = Host.Services.GetRequiredService<IDbContextFactory<OmniCardDbContext>>().CreateDbContext())
                invCtx.Database.EnsureCreated();

            splash.SetStatus("Preparing scan cache...");
            Directory.CreateDirectory(DataPathServiceInstance.ScansDirectory);

            // Initialize scan image cache
            var scanImageCache = Host.Services.GetRequiredService<ScanImageCache>();
            ScanImageCache.Initialize(scanImageCache);

            // Initialize card art cache
            var cardArtCache = Host.Services.GetRequiredService<CardArtCache>();
            CardArtCache.Initialize(cardArtCache);

            // Clean up temp scan files from previous sessions (crash recovery)
            var tempScansDir = scanImageCache.TempScansDirectory;
            if (Directory.Exists(tempScansDir))
            {
                var tempFiles = Directory.GetFiles(tempScansDir);
                if (tempFiles.Length > 0)
                {
                    foreach (var file in tempFiles)
                    {
                        try { File.Delete(file); }
                        catch { /* best effort cleanup */ }
                    }
                    migrationLogger.LogInformation("Cleaned up {Count} temp scan file(s) from previous session", tempFiles.Length);
                }
            }
            Directory.CreateDirectory(tempScansDir);
        });

        splash.SetStatus("Starting application...");
        Host.Start();

        // Start phone scanner connection (non-blocking)
        var webScanner = Host.Services.GetRequiredService<WebScannerService>();
        _ = webScanner.StartAsync();

        // Initialize set symbol converter with cached service
        var setSymbolCache = Host.Services.GetRequiredService<SetSymbolCache>();
        SetSymbol.Initialize(setSymbolCache);

        // Price refresh is manual only (Collection > Refresh Prices) to keep startup light.
        splash.Close();
    }

    internal static void EnsureHashCorrectionsTable(Microsoft.Data.Sqlite.SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS HashCorrections (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ScanHash INTEGER NOT NULL,
                CorrectCardId TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UNIQUE(ScanHash)
            )
            """;
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_HashCorrections_ScanHash ON HashCorrections(ScanHash)";
        cmd.ExecuteNonQuery();
    }

    private static void EnsureHashCorrectionsInGameDbs(string dataDirectory, Microsoft.Extensions.Logging.ILogger logger)
    {
        foreach (var dbName in new[] { "scryfall.db", "optcg.db" })
        {
            var dbPath = Path.Combine(dataDirectory, dbName);
            if (!File.Exists(dbPath))
                continue;

            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            EnsureHashCorrectionsTable(conn);
            logger.LogInformation("HashCorrections table ensured in {Database}", dbName);
        }
    }

    private static string InitSettingsDirectory()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OmniCard");
        Directory.CreateDirectory(dir);

        var appsettingsPath = Path.Combine(dir, "appsettings.json");
        if (!File.Exists(appsettingsPath))
        {
            File.WriteAllText(appsettingsPath, """
                {
                  "Serilog": {
                    "MinimumLevel": {
                      "Default": "Debug",
                      "Override": {
                        "Microsoft.EntityFrameworkCore": "Warning"
                      }
                    },
                    "WriteTo": [
                      {
                        "Name": "Console",
                        "Args": {
                          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"
                        }
                      }
                    ],
                    "Enrich": [
                      "FromLogContext"
                    ]
                  },
                  "Display": {
                    "CardDetailFontSize": 14,
                    "Theme": "Dark"
                  },
                  "eBay": {
                    "AppId": "",
                    "CertId": "",
                    "DevId": "",
                    "RuName": "",
                    "AcceptUrl": "",
                    "Environment": "sandbox"
                  },
                  "Scryfall": {
                    "Languages": ["en"]
                  },
                  "WebCompanion": {
                    "BaseUrl": "https://localhost:8081"
                  }
                }
                """);
        }

        return dir;
    }
}
