using System.Globalization;
using System.Reflection;
using NTwain;
using NTwain.Data;
using OmniCard.Scanner;

namespace OmniCard.ScannerHost;

static class Program
{
    private static string? _outputPath;
    private static int _exitCode = 3; // default: no image
    private static Form? _hiddenForm;

    [STAThread]
    static int Main(string[] args)
    {
        string? scannerName = null;
        int dpi = 200;
        bool showUI = false;
        bool foil = false;
        float foilBrightness = ScanSettings.DefaultFoilBrightness;
        float foilContrast = ScanSettings.DefaultFoilContrast;
        string? settingsPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--settings" when i + 1 < args.Length:
                    settingsPath = args[++i];
                    break;
                case "--scanner" when i + 1 < args.Length:
                    scannerName = args[++i];
                    break;
                case "--output" when i + 1 < args.Length:
                    _outputPath = args[++i];
                    break;
                case "--dpi" when i + 1 < args.Length:
                    int.TryParse(args[++i], out dpi);
                    break;
                case "--show-ui":
                    showUI = true;
                    break;
                case "--foil":
                    foil = true;
                    break;
                case "--foil-brightness" when i + 1 < args.Length:
                    float.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out foilBrightness);
                    break;
                case "--foil-contrast" when i + 1 < args.Length:
                    float.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out foilContrast);
                    break;
            }
        }

        if (scannerName is null || _outputPath is null)
        {
            Console.Error.WriteLine("Usage: OmniCard.ScannerHost --scanner <name> --output <path> [--dpi N] [--show-ui] [--foil]");
            return 2;
        }

        try
        {
            var appId = TWIdentity.CreateFromAssembly(DataGroups.Image, Assembly.GetExecutingAssembly());
            var session = new TwainSession(appId);

            session.TransferReady += (_, _) => { };
            session.DataTransferred += OnDataTransferred;
            session.TransferError += (_, e) =>
            {
                Console.Error.WriteLine($"Transfer error: {e.ReturnCode}");
                _exitCode = 2;
                _hiddenForm?.Close();
            };
            session.SourceDisabled += (_, _) =>
            {
                _hiddenForm?.Close();
            };

            session.Open();

            var source = session.OfType<DataSource>()
                .FirstOrDefault(s => string.Equals(s.Name, scannerName, StringComparison.OrdinalIgnoreCase));

            if (source is null)
            {
                Console.Error.WriteLine($"Scanner not found: {scannerName}");
                session.Close();
                return 1;
            }

            source.Open();
            // Same shared applier the in-process ScannerService uses, so both paths configure
            // the scanner identically. dpi of 0 => native default (resolved inside the applier).
            var settings = new ScanSettings(dpi, foil, foilBrightness, foilContrast);
            ScanSettingsApplier.Apply(
                source.Capabilities,
                settings,
                onDebug: msg => Console.Error.WriteLine(msg));

            // Layer the scanner's saved capability profile on top of the baseline (same as the
            // in-process path). The profile is a self-contained JSON file written by the app.
            if (settingsPath is not null)
            {
                try
                {
                    var profile = System.Text.Json.JsonSerializer.Deserialize<OmniCard.Models.ScannerProfile>(
                        File.ReadAllText(settingsPath));
                    if (profile is not null)
                        CapabilityProfileApplier.Apply(source, profile.Capabilities, msg => Console.Error.WriteLine(msg));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Could not apply scanner profile: {ex.Message}");
                }
            }

            // Create a hidden form to provide a proper window handle and
            // message pump for the TWAIN driver. Some drivers (e.g., Canon RS40)
            // crash without a valid HWND for message routing.
            _hiddenForm = new Form { Visible = false, ShowInTaskbar = false };
            var hwnd = _hiddenForm.Handle; // force handle creation

            var mode = showUI ? SourceEnableMode.ShowUI : SourceEnableMode.NoUI;
            source.Enable(mode, showUI, hwnd);

            // Run message loop until scan completes or fails
            Application.Run(_hiddenForm);

            source.Close();
            session.Close();

            return _exitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Scanner error: {ex.Message}");
            return 2;
        }
    }

    private static void OnDataTransferred(object? sender, DataTransferredEventArgs e)
    {
        try
        {
            using var stream = e.GetNativeImageStream();
            if (stream is null)
            {
                Console.Error.WriteLine("No image data in transfer");
                _exitCode = 3;
                return;
            }

            var dir = Path.GetDirectoryName(_outputPath);
            if (dir is not null)
                Directory.CreateDirectory(dir);

            using var file = File.Create(_outputPath!);
            stream.CopyTo(file);
            _exitCode = 0;
            Console.Out.WriteLine($"Image written to {_outputPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to save image: {ex.Message}");
            _exitCode = 2;
        }
    }

}
