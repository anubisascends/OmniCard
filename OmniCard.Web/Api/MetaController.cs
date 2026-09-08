using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Interfaces;

namespace OmniCard.Web.Api;

/// <summary>App metadata for the SPA shell: the list of supported games (for the game selector) and
/// the third-party software inventory (Administration ▸ Components).</summary>
public sealed class MetaController(IEnumerable<ICardGameService> gameServices) : ApiControllerBase
{
    [HttpGet("games")]
    public ActionResult<IReadOnlyList<GameDto>> Games() =>
        gameServices.Select(g => DtoMapping.ToDto(g.Game)).ToList();

    /// <summary>
    /// The software/components inventory shown in Administration ▸ Components: name, version, license,
    /// and links. Keep this in sync with <c>THIRD-PARTY-NOTICES.txt</c> and the project
    /// <c>PackageReference</c>s / <c>package.json</c> when packages are added, removed, or upgraded.
    /// </summary>
    [HttpGet("components")]
    public ActionResult<IReadOnlyList<ComponentDto>> Components()
    {
        const string mit = "https://opensource.org/license/mit";
        const string apache2 = "https://www.apache.org/licenses/LICENSE-2.0";
        const string bsd3 = "https://opensource.org/license/bsd-3-clause";

        var appVersion =
            Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
            ?? "—";
        // Strip any build metadata suffix (e.g. "1.2.3+abc123") for display.
        var plusIndex = appVersion.IndexOf('+');
        if (plusIndex >= 0) appVersion = appVersion[..plusIndex];

        var components = new List<ComponentDto>
        {
            // ── Platform / runtime ──────────────────────────────────────────────
            new("Application", "OmniCard", appVersion, "Proprietary", "https://github.com/anubisascends/OmniCard", null),
            new("Platform", ".NET", Environment.Version.ToString(), "MIT", "https://dotnet.microsoft.com", mit),
            new("Platform", "ASP.NET Core", "10.0", "MIT", "https://learn.microsoft.com/aspnet/core", mit),
            new("Platform", "Microsoft.AspNetCore.OpenApi", "10.0.0", "MIT", "https://github.com/dotnet/aspnetcore", mit),

            // ── Backend libraries ───────────────────────────────────────────────
            new("Backend", "Entity Framework Core (SQL Server + SQLite)", "10.0.9", "MIT", "https://github.com/dotnet/efcore", mit),
            new("Backend", "Microsoft.Data.Sqlite", "10.0.9", "MIT", "https://github.com/dotnet/efcore", mit),
            new("Backend", "SQLitePCLRaw", "2.1.13", "Apache-2.0", "https://github.com/ericsink/SQLitePCL.raw", apache2),
            new("Backend", "Microsoft.Extensions.* (Hosting, Http, Logging, Options)", "10.0.9", "MIT", "https://github.com/dotnet/runtime", mit),
            new("Backend", "Serilog", "4.3.0", "Apache-2.0", "https://serilog.net", apache2),
            new("Backend", "Serilog.Sinks.Console", "6.1.1", "Apache-2.0", "https://github.com/serilog/serilog-sinks-console", apache2),
            new("Backend", "Serilog.Sinks.File", "6.0.0", "Apache-2.0", "https://github.com/serilog/serilog-sinks-file", apache2),
            new("Backend", "CommunityToolkit.Mvvm", "8.4.2", "MIT", "https://github.com/CommunityToolkit/dotnet", mit),
            new("Backend", "CsvHelper", "33.1.0", "MS-PL OR Apache-2.0", "https://github.com/JoshClose/CsvHelper", apache2),
            new("Backend", "AdysTech.CredentialManager", "3.1.0", "Apache-2.0", "https://github.com/mnottale/AdysTech.CredentialManager", apache2),

            // ── Imaging / OCR / documents ───────────────────────────────────────
            new("Imaging & OCR", "SkiaSharp", "4.150.1", "MIT", "https://github.com/mono/SkiaSharp", mit),
            new("Imaging & OCR", "Tesseract (.NET wrapper)", "5.2.0", "Apache-2.0", "https://github.com/charlesw/tesseract", apache2),
            new("Imaging & OCR", "Tesseract OCR engine (tesseract, leptonica)", "5.x", "Apache-2.0", "https://github.com/tesseract-ocr/tesseract", apache2),
            new("Imaging & OCR", "SharpVectors", "1.8.5", "BSD-3-Clause", "https://github.com/ElinamLLC/SharpVectors", bsd3),
            new("Documents", "QuestPDF", "2026.7.0", "MIT (Community) / dual-licensed", "https://www.questpdf.com", "https://www.questpdf.com/license/"),

            // ── Frontend (SPA) ──────────────────────────────────────────────────
            new("Frontend", "React", "18.3.1", "MIT", "https://react.dev", mit),
            new("Frontend", "React DOM", "18.3.1", "MIT", "https://react.dev", mit),
            new("Frontend", "React Router", "6.27.0", "MIT", "https://reactrouter.com", mit),
            new("Frontend", "MUI (Material UI)", "6.1.6", "MIT", "https://mui.com", mit),
            new("Frontend", "MUI X Data Grid", "7.22.2", "MIT", "https://mui.com/x/react-data-grid", mit),
            new("Frontend", "Emotion", "11.x", "MIT", "https://emotion.sh", mit),
            new("Frontend", "TanStack Query", "5.59.16", "MIT", "https://tanstack.com/query", mit),
            new("Frontend", "TypeScript", "5.6.3", "Apache-2.0", "https://www.typescriptlang.org", apache2),
            new("Frontend", "Vite", "5.4.10", "MIT", "https://vitejs.dev", mit),

            // ── Build tooling ───────────────────────────────────────────────────
            new("Build tooling", "MinVer", "7.0.0", "MIT", "https://github.com/adamralph/minver", mit),

            // ── Card data & imagery providers ───────────────────────────────────
            new("Data providers", "Scryfall (Magic: The Gathering data & images)", "API", "See provider terms", "https://scryfall.com", "https://scryfall.com/docs/api"),
            new("Data providers", "TCGCSV (Pokémon, Yu-Gi-Oh!, Final Fantasy TCG data)", "API", "See provider terms", "https://tcgcsv.com", "https://tcgcsv.com"),
        };

        return components;
    }
}
