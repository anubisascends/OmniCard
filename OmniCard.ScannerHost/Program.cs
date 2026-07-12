using System.Reflection;
using NTwain;
using NTwain.Data;

namespace OmniCard.ScannerHost;

static class Program
{
    private static string? _outputPath;
    private static bool _imageReceived;
    private static int _exitCode = 3; // default: no image
    private static Form? _hiddenForm;

    [STAThread]
    static int Main(string[] args)
    {
        string? scannerName = null;
        int dpi = 200;
        bool showUI = false;
        bool foil = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
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
            ApplySettings(source, dpi, foil);

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
            _imageReceived = true;
            _exitCode = 0;
            Console.Out.WriteLine($"Image written to {_outputPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to save image: {ex.Message}");
            _exitCode = 2;
        }
    }

    private static void ApplySettings(DataSource ds, int dpi, bool foil)
    {
        var caps = ds.Capabilities;

        // Pixel type: RGB
        try { if (caps.ICapPixelType.CanSet) caps.ICapPixelType.SetValue(PixelType.RGB); }
        catch { }

        // ICC profile: Embed
        try { if (caps.ICapICCProfile.CanSet) caps.ICapICCProfile.SetValue(IccProfile.Embed); }
        catch { }

        // Duplex: off
        try { if (caps.CapDuplexEnabled.CanSet) caps.CapDuplexEnabled.SetValue(BoolType.False); }
        catch { }

        // Resolution
        try { if (caps.ICapXResolution.CanSet) caps.ICapXResolution.SetValue((TWFix32)(float)dpi); }
        catch { }
        try { if (caps.ICapYResolution.CanSet) caps.ICapYResolution.SetValue((TWFix32)(float)dpi); }
        catch { }

        // Reset image processing
        try { if (caps.ICapAutoBright.CanReset) caps.ICapAutoBright.Reset(); } catch { }
        try { if (caps.ICapBrightness.CanReset) caps.ICapBrightness.Reset(); } catch { }
        try { if (caps.ICapContrast.CanReset) caps.ICapContrast.Reset(); } catch { }
        try { if (caps.ICapGamma.CanReset) caps.ICapGamma.Reset(); } catch { }
        try { if (caps.ICapHighlight.CanReset) caps.ICapHighlight.Reset(); } catch { }
        try { if (caps.ICapShadow.CanReset) caps.ICapShadow.Reset(); } catch { }

        // Foil adjustments
        if (foil)
        {
            try { if (caps.ICapAutoBright.CanSet) caps.ICapAutoBright.SetValue(BoolType.False); } catch { }
            try { if (caps.ICapBrightness.CanSet) caps.ICapBrightness.SetValue((TWFix32)(-200f)); } catch { }
            try { if (caps.ICapContrast.CanSet) caps.ICapContrast.SetValue((TWFix32)333.3333f); } catch { }
        }
    }
}
