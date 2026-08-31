using NTwain;
using NTwain.Data;

namespace OmniCard.Scanner;

/// <summary>
/// Applies a <see cref="ScanSettings"/> to a TWAIN source's capabilities. This is the single
/// source of truth for how OmniCard configures a scanner, called from both the in-process
/// <see cref="ScannerService"/> and the out-of-process ScannerHost (into which this file is
/// linked). Keeping one implementation avoids the two paths drifting apart (they previously
/// had divergent DPI logic and only the in-process path pinned XferCount / AutoScan).
///
/// Every capability write is guarded by <c>CanSet</c> and swallowed on failure: a scanner that
/// doesn't support a given capability must not fault the whole scan. Logging is optional so this
/// file can be linked into the dependency-light ScannerHost without pulling in a logging package.
/// </summary>
public static class ScanSettingsApplier
{
    public static void Apply(
        ICapabilities caps,
        ScanSettings settings,
        Action<string>? onInfo = null,
        Action<string>? onDebug = null)
    {
        TrySetPixelType(caps, onDebug);
        TrySetColorProfile(caps, onDebug);
        TryDisableDuplex(caps, onInfo, onDebug);
        TryResetImageProcessing(caps, onDebug);
        TrySetXferCount(caps, -1, onDebug);
        TryEnableAutoScan(caps, onDebug);
        TrySetResolution(caps, settings.Dpi, onDebug);

        if (settings.Foil)
        {
            TrySetAutoBright(caps, false, onDebug);
            TrySetBrightness(caps, settings.FoilBrightness, onDebug);
            TrySetContrast(caps, settings.FoilContrast, onDebug);
            onInfo?.Invoke(
                $"Foil mode: brightness {settings.FoilBrightness}, auto-bright disabled, contrast {settings.FoilContrast}");
        }
    }

    /// <summary>Set X/Y resolution. When <paramref name="dpi"/> is 0, use the source's native default.</summary>
    private static void TrySetResolution(ICapabilities caps, int dpi, Action<string>? onDebug)
    {
        if (dpi > 0)
        {
            try { if (caps.ICapXResolution.CanSet) caps.ICapXResolution.SetValue((TWFix32)(float)dpi); }
            catch (Exception ex) { onDebug?.Invoke($"Cannot set XResolution: {ex.Message}"); }

            try { if (caps.ICapYResolution.CanSet) caps.ICapYResolution.SetValue((TWFix32)(float)dpi); }
            catch (Exception ex) { onDebug?.Invoke($"Cannot set YResolution: {ex.Message}"); }
        }
        else
        {
            try { if (caps.ICapXResolution.CanSet) caps.ICapXResolution.SetValue(caps.ICapXNativeResolution.GetDefault()); }
            catch (Exception ex) { onDebug?.Invoke($"Cannot set XResolution to native default: {ex.Message}"); }

            try { if (caps.ICapYResolution.CanSet) caps.ICapYResolution.SetValue(caps.ICapYNativeResolution.GetDefault()); }
            catch (Exception ex) { onDebug?.Invoke($"Cannot set YResolution to native default: {ex.Message}"); }
        }
    }

    private static void TrySetBrightness(ICapabilities caps, float value, Action<string>? onDebug)
    {
        try { if (caps.ICapBrightness.CanSet) caps.ICapBrightness.SetValue((TWFix32)value); }
        catch (Exception ex) { onDebug?.Invoke($"Cannot set Brightness to {value}: {ex.Message}"); }
    }

    private static void TrySetContrast(ICapabilities caps, float value, Action<string>? onDebug)
    {
        try { if (caps.ICapContrast.CanSet) caps.ICapContrast.SetValue((TWFix32)value); }
        catch (Exception ex) { onDebug?.Invoke($"Cannot set Contrast to {value}: {ex.Message}"); }
    }

    private static void TrySetAutoBright(ICapabilities caps, bool enabled, Action<string>? onDebug)
    {
        try { if (caps.ICapAutoBright.CanSet) caps.ICapAutoBright.SetValue(enabled ? BoolType.True : BoolType.False); }
        catch (Exception ex) { onDebug?.Invoke($"Cannot set AutoBright to {enabled}: {ex.Message}"); }
    }

    private static void TryResetImageProcessing(ICapabilities caps, Action<string>? onDebug)
    {
        try { if (caps.ICapAutoBright.CanReset) caps.ICapAutoBright.Reset(); }
        catch (Exception ex) { onDebug?.Invoke($"Cannot reset AutoBright: {ex.Message}"); }

        try { if (caps.ICapBrightness.CanReset) caps.ICapBrightness.Reset(); }
        catch (Exception ex) { onDebug?.Invoke($"Cannot reset Brightness: {ex.Message}"); }

        try { if (caps.ICapContrast.CanReset) caps.ICapContrast.Reset(); }
        catch (Exception ex) { onDebug?.Invoke($"Cannot reset Contrast: {ex.Message}"); }

        try { if (caps.ICapGamma.CanReset) caps.ICapGamma.Reset(); }
        catch (Exception ex) { onDebug?.Invoke($"Cannot reset Gamma: {ex.Message}"); }

        try { if (caps.ICapHighlight.CanReset) caps.ICapHighlight.Reset(); }
        catch (Exception ex) { onDebug?.Invoke($"Cannot reset Highlight: {ex.Message}"); }

        try { if (caps.ICapShadow.CanReset) caps.ICapShadow.Reset(); }
        catch (Exception ex) { onDebug?.Invoke($"Cannot reset Shadow: {ex.Message}"); }
    }

    private static void TrySetPixelType(ICapabilities caps, Action<string>? onDebug)
    {
        try { if (caps.ICapPixelType.CanSet) caps.ICapPixelType.SetValue(PixelType.RGB); }
        catch (Exception ex) { onDebug?.Invoke($"Cannot set PixelType to RGB: {ex.Message}"); }
    }

    private static void TrySetColorProfile(ICapabilities caps, Action<string>? onDebug)
    {
        try { if (caps.ICapICCProfile.CanSet) caps.ICapICCProfile.SetValue(IccProfile.Embed); }
        catch (Exception ex) { onDebug?.Invoke($"Cannot set ICC profile: {ex.Message}"); }
    }

    private static void TrySetXferCount(ICapabilities caps, int count, Action<string>? onDebug)
    {
        // Pin the transfer count explicitly (-1 = unlimited) so a previous app's leftover value
        // (e.g. a low count from a single-page job) doesn't cut the ADF batch short.
        try { if (caps.CapXferCount.CanSet) caps.CapXferCount.SetValue(count); }
        catch (Exception ex) { onDebug?.Invoke($"Cannot set XferCount to {count}: {ex.Message}"); }
    }

    private static void TryEnableAutoScan(ICapabilities caps, Action<string>? onDebug)
    {
        // ADF-only scanners (e.g. Canon RS40) need AutoScan=TRUE to keep pulling cards from the
        // feeder automatically instead of stopping after one; pin it on so a previous app leaving
        // it off doesn't stick.
        try { if (caps.CapAutoScan.CanSet) caps.CapAutoScan.SetValue(BoolType.True); }
        catch (Exception ex) { onDebug?.Invoke($"Cannot enable AutoScan: {ex.Message}"); }
    }

    private static void TryDisableDuplex(ICapabilities caps, Action<string>? onInfo, Action<string>? onDebug)
    {
        try
        {
            if (caps.CapDuplexEnabled.CanSet)
            {
                caps.CapDuplexEnabled.SetValue(BoolType.False);
                onInfo?.Invoke("Duplex scanning disabled (single-sided)");
            }
        }
        catch (Exception ex) { onDebug?.Invoke($"Cannot disable duplex: {ex.Message}"); }
    }
}
