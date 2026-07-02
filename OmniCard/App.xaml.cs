using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using OmniCard.Models;
using System.IO;
using System.Windows;
using OmniCard.Data;
using OmniCard.Helpers;
using OmniCard.Services;
using OmniCard.Views.Card;
using OmniCard.Views.CollectionCardEditor;
using OmniCard.Views.Connection;
using OmniCard.Views.CoverArtPicker;
using OmniCard.Views.CsvImport;
using OmniCard.Views.HashPreview;
using OmniCard.Views.Root;
using OmniCard.Views.EbayAuth;
using OmniCard.Views.DataLocation;
using OmniCard.Views.SortFilterBuilder;
using OmniCard.Views.SealedProductEditor;
using OmniCard.Views.StorageManager;

namespace OmniCard;

public partial class App : Application
{
    private static readonly DataPathService DataPathServiceInstance = new(AppContext.BaseDirectory);

    public static IHost Host { get; } = new HostBuilder()
        .ConfigureAppConfiguration((_, config) =>
        {
            config.SetBasePath(AppContext.BaseDirectory);
            config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
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
            services.AddSingleton<SealedProductViewModel>();
            services.AddSingleton<RootViewModel>();
            services.AddSingleton<ScannerService>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<IPerceptualHashService, PerceptualHashService>();
            services.AddSingleton<IOcrMatchingService, OcrMatchingService>();
            services.AddSingleton<ICardService, CardSevice>();
            services.AddSingleton<ScanImageCache>();
            services.AddHttpClient();

            // Register data path service
            services.AddSingleton<IDataPathService>(DataPathServiceInstance);
            services.AddSingleton<IDataMigrationService, DataMigrationService>();

            // MTG (Scryfall)
            services.AddDbContextFactory<ScryfallDbContext>(options =>
                options.UseSqlite($"Data Source={Path.Combine(DataPathServiceInstance.DataDirectory, "scryfall.db")}"));
            services.AddDbContextFactory<CollectionDbContext>(options =>
                options.UseSqlite($"Data Source={Path.Combine(DataPathServiceInstance.DataDirectory, "collection.db")}"));
            services.AddSingleton<SetSymbolCache>();
            services.AddSingleton<ICardGameService, ScryfallService>();
            services.AddSingleton<IScryfallService>(sp => (ScryfallService)sp.GetRequiredService<IEnumerable<ICardGameService>>().First(s => s.Game == Models.CardGame.Mtg));

            // One Piece (OPTCG)
            services.AddDbContextFactory<OptcgDbContext>(options =>
                options.UseSqlite($"Data Source={Path.Combine(DataPathServiceInstance.DataDirectory, "optcg.db")}"));
            services.AddSingleton<ICardGameService, OptcgService>();

            // Sealed products
            services.AddDbContextFactory<SealedProductDbContext>(options =>
                options.UseSqlite($"Data Source={Path.Combine(DataPathServiceInstance.DataDirectory, "sealed_products.db")}"));
            services.AddSingleton<ISealedProductService, SealedProductService>();

            // Storage containers
            services.AddSingleton<IStorageContainerService, StorageContainerService>();

            // Sort/filter presets
            services.AddSingleton<ICollectionPresetService, CollectionPresetService>();

            // CSV export/import
            services.AddSingleton<ICsvExportImportService, CsvExportImportService>();

            // eBay OAuth
            services.AddSingleton<ICredentialStore, CredentialStore>();
            services.AddSingleton<IEbayAuthService, EbayAuthService>();

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
            services.AddTransient<DataLocationView>();
            services.AddTransient<DataLocationViewModel>();
            services.AddTransient<CoverArtPickerView>();
            services.AddTransient<CoverArtPickerViewModel>();
            services.AddTransient<OmniCard.Views.MoveToLocation.MoveToLocationView>();
            services.AddTransient<OmniCard.Views.MoveToLocation.MoveToLocationViewModel>();
            services.AddTransient<SealedProductTemplateEditorView>();
            services.AddTransient<SealedProductTemplateEditorViewModel>();
            services.AddTransient<AddSealedProductView>();
            services.AddTransient<AddSealedProductViewModel>();
            services.AddTransient<CrackProductView>();
            services.AddTransient<CrackProductViewModel>();
        })
        .Build();

    protected override void OnStartup(StartupEventArgs e)
    {
        // Check data directory is reachable
        var dataDir = DataPathServiceInstance.DataDirectory;
        if (!Directory.Exists(dataDir))
        {
            try
            {
                Directory.CreateDirectory(dataDir);
            }
            catch (Exception ex)
            {
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

        // Run collection migration before starting the host
        var collectionDbFactory = Host.Services.GetRequiredService<IDbContextFactory<CollectionDbContext>>();
        var loggerFactory = Host.Services.GetRequiredService<ILoggerFactory>();
        var migrationLogger = loggerFactory.CreateLogger("CollectionMigration");
        CollectionMigrationService.MigrateIfNeeded(dataDir, collectionDbFactory, migrationLogger);

        // Ensure ScanImagePath column exists (added in v0.3)
        EnsureScanImagePathColumn(dataDir, collectionDbFactory, migrationLogger);

        // Ensure StorageContainer schema (added in v0.4)
        EnsureStorageContainerSchema(dataDir, collectionDbFactory, migrationLogger);

        // Ensure HashCorrections table in game databases (added in v0.5)
        EnsureHashCorrectionsInGameDbs(dataDir, migrationLogger);

        // Ensure Color/CardType columns on Cards table (added for sort/filter)
        EnsureColorCardTypeColumns(dataDir, collectionDbFactory, migrationLogger);

        // Ensure CoverCardId column on StorageContainers (added for collection redesign)
        EnsureCoverCardIdColumn(dataDir, collectionDbFactory, migrationLogger);

        // Initialize sealed products database
        using var sealedCtx = Host.Services.GetRequiredService<IDbContextFactory<SealedProductDbContext>>().CreateDbContext();
        sealedCtx.Database.EnsureCreated();

        // Backfill Color/CardType for existing cards
        var gameServices = Host.Services.GetRequiredService<IEnumerable<ICardGameService>>();
        BackfillColorCardType(collectionDbFactory, gameServices, migrationLogger);

        Directory.CreateDirectory(DataPathServiceInstance.ScansDirectory);

        // Initialize scan image cache
        var scanImageCache = Host.Services.GetRequiredService<ScanImageCache>();
        ScanImageCache.Initialize(scanImageCache);

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

        Host.Start();

        // Initialize set symbol converter with cached service
        var setSymbolCache = Host.Services.GetRequiredService<SetSymbolCache>();
        SetSymbol.Initialize(setSymbolCache);
    }

    private static void EnsureScanImagePathColumn(
        string dataDirectory,
        IDbContextFactory<CollectionDbContext> factory,
        Microsoft.Extensions.Logging.ILogger logger)
    {
        var dbPath = Path.Combine(dataDirectory, "collection.db");
        if (!File.Exists(dbPath))
            return;

        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Cards') WHERE name = 'ScanImagePath'";
        var exists = cmd.ExecuteScalar() is long count && count > 0;

        if (!exists)
        {
            cmd.CommandText = "ALTER TABLE Cards ADD COLUMN ScanImagePath TEXT";
            cmd.ExecuteNonQuery();
            logger.LogInformation("Added ScanImagePath column to Cards table");
        }
    }

    internal static void EnsureStorageContainerSchema(
        string dataDirectory,
        IDbContextFactory<CollectionDbContext> factory,
        Microsoft.Extensions.Logging.ILogger logger)
    {
        var dbPath = Path.Combine(dataDirectory, "collection.db");
        if (!File.Exists(dbPath))
            return;

        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        EnsureStorageContainerSchema(conn);
        logger.LogInformation("Storage container schema migration complete");
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

    internal static void EnsureStorageContainerSchema(Microsoft.Data.Sqlite.SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();

        // 1. Create StorageContainers table
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS StorageContainers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                ContainerType TEXT NOT NULL,
                IsSystem INTEGER NOT NULL DEFAULT 0,
                SortOrder INTEGER NOT NULL DEFAULT 0
            )
            """;
        cmd.ExecuteNonQuery();

        // 2. Seed Bulk container
        cmd.CommandText = "INSERT OR IGNORE INTO StorageContainers (Name, ContainerType, IsSystem, SortOrder) VALUES ('Bulk', 'Bulk', 1, 0)";
        cmd.ExecuteNonQuery();

        // 3. Add columns to Cards table
        foreach (var col in new[] { ("ContainerId", "INTEGER"), ("Page", "INTEGER"), ("Slot", "INTEGER"), ("Section", "TEXT") })
        {
            cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('Cards') WHERE name = '{col.Item1}'";
            if ((long)cmd.ExecuteScalar()! == 0)
            {
                cmd.CommandText = $"ALTER TABLE Cards ADD COLUMN {col.Item1} {col.Item2}";
                cmd.ExecuteNonQuery();
            }
        }

        // 4. Default existing cards to Bulk
        cmd.CommandText = "UPDATE Cards SET ContainerId = (SELECT Id FROM StorageContainers WHERE IsSystem = 1) WHERE ContainerId IS NULL";
        cmd.ExecuteNonQuery();
    }

    internal static void EnsureColorCardTypeColumns(Microsoft.Data.Sqlite.SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        foreach (var col in new[] { "Color", "CardType" })
        {
            cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('Cards') WHERE name = '{col}'";
            if ((long)cmd.ExecuteScalar()! == 0)
            {
                cmd.CommandText = $"ALTER TABLE Cards ADD COLUMN {col} TEXT";
                cmd.ExecuteNonQuery();
            }
        }

        cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_Cards_Color ON Cards(Color)";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_Cards_CardType ON Cards(CardType)";
        cmd.ExecuteNonQuery();
    }

    internal static void EnsureCoverCardIdColumn(Microsoft.Data.Sqlite.SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('StorageContainers') WHERE name = 'CoverCardId'";
        if ((long)cmd.ExecuteScalar()! == 0)
        {
            cmd.CommandText = "ALTER TABLE StorageContainers ADD COLUMN CoverCardId INTEGER";
            cmd.ExecuteNonQuery();
        }
    }

    private static void EnsureColorCardTypeColumns(
        string dataDirectory,
        IDbContextFactory<CollectionDbContext> factory,
        Microsoft.Extensions.Logging.ILogger logger)
    {
        var dbPath = Path.Combine(dataDirectory, "collection.db");
        if (!File.Exists(dbPath))
            return;

        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        EnsureColorCardTypeColumns(conn);
        logger.LogInformation("Color/CardType column migration complete");
    }

    private static void EnsureCoverCardIdColumn(
        string dataDirectory,
        IDbContextFactory<CollectionDbContext> factory,
        Microsoft.Extensions.Logging.ILogger logger)
    {
        var dbPath = Path.Combine(dataDirectory, "collection.db");
        if (!File.Exists(dbPath))
            return;

        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        EnsureCoverCardIdColumn(conn);
        logger.LogInformation("Added CoverCardId column to StorageContainers table");
    }

    private static void BackfillColorCardType(
        IDbContextFactory<CollectionDbContext> collectionFactory,
        IEnumerable<ICardGameService> gameServices,
        Microsoft.Extensions.Logging.ILogger logger)
    {
        using var ctx = collectionFactory.CreateDbContext();

        // On first run the collection DB may not have the Cards table yet
        // (it gets created later by CardService). Skip backfill in that case.
        try
        {
            if (!ctx.Database.GetAppliedMigrations().Any() && !ctx.Database.CanConnect())
                return;
        }
        catch { /* CanConnect may throw if DB is brand new */ }

        List<CollectionCard> cardsToFill;
        try
        {
            cardsToFill = ctx.Cards.Where(c => c.Color == null || c.CardType == null).ToList();
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            // Table doesn't exist yet — nothing to backfill
            return;
        }
        if (cardsToFill.Count == 0)
            return;

        var services = gameServices.ToDictionary(s => s.Game);
        var filled = 0;

        foreach (var card in cardsToFill)
        {
            if (!services.TryGetValue(card.Game, out var gameService))
                continue;

            var sourceCard = gameService.FindCardById(card.GameCardId);
            if (sourceCard is null)
                continue;

            var match = new CardMatch { Source = sourceCard };
            card.Color ??= CardAttributeExtractor.ExtractColor(match, card.Game);
            card.CardType ??= CardAttributeExtractor.ExtractCardType(match, card.Game);
            filled++;
        }

        if (filled > 0)
        {
            ctx.SaveChanges();
            logger.LogInformation("Backfilled Color/CardType for {Count} cards", filled);
        }
    }
}
