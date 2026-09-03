using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.Input;

namespace OmniCard.Views.About;

/// <summary>
/// Backs the About dialog. Surfaces app identity/version (read from the entry assembly's
/// metadata — see <c>Directory.Build.props</c>) and the third-party library attributions
/// shown in the licenses list. Keep <see cref="Attributions"/> in sync with
/// <c>THIRD-PARTY-NOTICES.txt</c> when packages are added or removed.
/// </summary>
public sealed partial class AboutViewModel : ViewModel
{
    public string ProductName { get; }
    public string Version { get; }
    public string Copyright { get; }
    public string Framework { get; }

    public string Description =>
        "OmniCard is a Windows desktop app for scanning and managing trading card collections. "
        + "It identifies physical cards scanned via TWAIN scanner or phone camera using perceptual "
        + "image hashing and OCR, then tracks them across storage locations, sealed-product "
        + "inventory, and sales/fulfillment (including eBay listing).";

    /// <summary>Third-party libraries bundled with OmniCard and their licenses.</summary>
    public ObservableCollection<Attribution> Attributions { get; }

    public AboutViewModel()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var product = asm.GetCustomAttribute<AssemblyProductAttribute>()?.Product;
        var copyright = asm.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;

        ProductName = string.IsNullOrWhiteSpace(product) ? "OmniCard" : product;
        // Single source of truth for the running version (stamped by MinVer from the git tag).
        Version = Helpers.AppVersionInfo.Version;
        Copyright = string.IsNullOrWhiteSpace(copyright) ? "Copyright © 2026 Andrew Riebe" : copyright;
        Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

        Attributions = new ObservableCollection<Attribution>(BuildAttributions());
    }

    [RelayCommand]
    private static void OpenLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Best-effort: a missing/blocked browser shouldn't crash the About dialog.
        }
    }

    private static IEnumerable<Attribution> BuildAttributions() =>
    [
        new("MaterialDesignThemes", "5.3.2", "MIT", "https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit"),
        new("CommunityToolkit.Mvvm", "8.4.2", "MIT (.NET Foundation)", "https://github.com/CommunityToolkit/dotnet"),
        new("Entity Framework Core (+ Sqlite provider)", "10.0.9", "MIT (.NET Foundation)", "https://github.com/dotnet/efcore"),
        new("Microsoft.Data.Sqlite", "10.0.9", "MIT (.NET Foundation)", "https://github.com/dotnet/efcore"),
        new("SQLitePCLRaw (bundle_e_sqlite3, native SQLite)", "2.1.13", "Apache-2.0", "https://github.com/ericsink/SQLitePCL.raw"),
        new("Microsoft.Extensions.* (Hosting, Http, Logging, Options)", "10.0.9", "MIT (Microsoft)", "https://github.com/dotnet/runtime"),
        new("Microsoft.AspNetCore.SignalR.Client", "10.0.9", "MIT (Microsoft)", "https://github.com/dotnet/aspnetcore"),
        new("Microsoft.Xaml.Behaviors.Wpf", "1.1.142", "MIT (Microsoft)", "https://github.com/microsoft/XamlBehaviorsWpf"),
        new("Microsoft.Web.WebView2", "1.0.4022.49", "Microsoft WebView2 SDK — proprietary (Microsoft Software License Terms)", "https://developer.microsoft.com/microsoft-edge/webview2/"),
        new("Serilog (+ Console/File sinks, Extensions)", "4.3.0", "Apache-2.0", "https://github.com/serilog/serilog"),
        new("Tesseract (OCR wrapper + native engine, eng.traineddata)", "5.2.0", "Apache-2.0", "https://github.com/charlesw/tesseract"),
        new("SkiaSharp", "4.150.1", "MIT", "https://github.com/mono/SkiaSharp"),
        new("QuestPDF", "2026.7.0", "QuestPDF Community MIT License (dual-licensed; Professional/Enterprise required above the revenue threshold)", "https://www.questpdf.com/license/"),
        new("NTwain", "3.7.6", "MIT", "https://github.com/soukoku/ntwain"),
        new("CsvHelper", "33.1.0", "MS-PL OR Apache-2.0 (dual)", "https://github.com/JoshClose/CsvHelper"),
        new("QRCoder", "1.6.0", "MIT", "https://github.com/codebude/QRCoder"),
        new("SharpVectors.Wpf", "1.8.5", "BSD-3-Clause", "https://github.com/ElinamLLC/SharpVectors"),
        new("VirtualizingWrapPanel", "2.5.3", "MIT", "https://github.com/sbaeumlisberger/VirtualizingWrapPanel"),
        new("AdysTech.CredentialManager", "3.1.0", "Apache-2.0", "https://github.com/mnottale/AdysTech.CredentialManager"),
        new("MinVer (build-time versioning tool; not shipped)", "7.0.0", "MIT", "https://github.com/adamralph/minver"),
    ];
}

/// <summary>A single third-party library attribution row shown in the About dialog.</summary>
public sealed record Attribution(string Name, string Version, string License, string Url);
