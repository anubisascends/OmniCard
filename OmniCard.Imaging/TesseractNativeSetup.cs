using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Tesseract;

namespace OmniCard.Imaging;

/// <summary>
/// The Tesseract NuGet package (charlesw, 5.2.0) only bundles Windows binaries. On Linux its loader looks
/// for <c>x64/libleptonica-1.82.0.so</c> and <c>x64/libtesseract50.so</c> under a search path and P/Invokes
/// <c>libdl</c>, none of which exist under those names. This maps them onto the distribution's own
/// Tesseract 5 / Leptonica packages (Debian/Ubuntu: <c>libtesseract5</c> + <c>liblept5</c> or
/// <c>libleptonica6</c>) through symlinks in a temp folder, so deployments need no file patching.
/// No-op on Windows, where the bundled binaries are used as before.
/// </summary>
internal static class TesseractNativeSetup
{
    private static readonly object Gate = new();
    private static bool _configured;

    private static readonly string[] LibraryDirectories =
    [
        "/usr/lib/x86_64-linux-gnu", "/usr/lib/aarch64-linux-gnu", "/usr/lib64", "/usr/lib", "/usr/local/lib",
    ];

    public static void EnsureConfigured(ILogger logger)
    {
        if (!OperatingSystem.IsLinux())
            return;

        lock (Gate)
        {
            if (_configured)
                return;
            _configured = true;

            // glibc 2.34+ ships dlopen in libc; only libdl.so.2 (no unversioned libdl.so) is installed.
            NativeLibrary.SetDllImportResolver(typeof(TesseractEngine).Assembly, (name, _, _) =>
                name is "libdl" or "libdl.so" && NativeLibrary.TryLoad("libdl.so.2", out var handle) ? handle : IntPtr.Zero);

            var tesseract = FindLibrary("libtesseract.so.5");
            var leptonica = FindLibrary("liblept.so.5") ?? FindLibrary("libleptonica.so.6");
            if (tesseract is null || leptonica is null)
            {
                logger.LogWarning("Tesseract/Leptonica system libraries not found (install libtesseract5 and liblept5) — OCR will be unavailable");
                return;
            }

            try
            {
                var root = Path.Combine(Path.GetTempPath(), "omnicard-tesseract");
                // The wrapper probes "<search path>/x64" for 64-bit processes.
                var platformDir = Path.Combine(root, "x64");
                Directory.CreateDirectory(platformDir);
                Link(Path.Combine(platformDir, "libleptonica-1.82.0.so"), leptonica);
                Link(Path.Combine(platformDir, "libtesseract50.so"), tesseract);
                TesseractEnviornment.CustomSearchPath = root;
                logger.LogDebug("Tesseract native libraries mapped: {Tesseract}, {Leptonica}", tesseract, leptonica);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to map Tesseract native libraries — OCR will be unavailable");
            }
        }
    }

    private static string? FindLibrary(string fileName) =>
        LibraryDirectories.Select(dir => Path.Combine(dir, fileName)).FirstOrDefault(File.Exists);

    private static void Link(string linkPath, string target)
    {
        var existing = new FileInfo(linkPath);
        if (existing.LinkTarget == target)
            return;
        if (existing.Exists || existing.LinkTarget is not null)
            existing.Delete();
        try
        {
            File.CreateSymbolicLink(linkPath, target);
        }
        catch (IOException) when (new FileInfo(linkPath).LinkTarget == target)
        {
            // Another process created the same link concurrently.
        }
    }
}
