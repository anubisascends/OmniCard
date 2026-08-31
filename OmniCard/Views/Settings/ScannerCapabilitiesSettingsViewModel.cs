using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NTwain;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Scanner;

namespace OmniCard.Views.Settings;

/// <summary>
/// Backs the Settings dialog's "Scanner Capabilities" section: pick a scanner, probe every
/// capability it advertises, and edit/persist the settable ones (plus OmniCard's DPI + foil
/// tuning) into a per-scanner profile. Saved caps are layered on top of OmniCard's baseline on
/// the next scan. OmniCard-managed caps (pixel type, transfer mechanics, ADF auto-scan, …) are
/// shown read-only so scanning/matching can't be broken.
/// </summary>
public partial class ScannerCapabilitiesSettingsViewModel(
    ScannerService scannerService,
    IScannerProfileService profileService,
    ILogger<ScannerCapabilitiesSettingsViewModel> logger) : ObservableObject
{
    public ObservableCollection<DataSource> AvailableScanners { get; } = [];

    [ObservableProperty]
    public partial DataSource? SelectedScanner { get; set; }

    /// <summary>The capability rows currently shown (filtered); the view groups them by
    /// <see cref="CapabilityEditItem.Group"/> via a CollectionViewSource in XAML.</summary>
    public ObservableCollection<CapabilityEditItem> Capabilities { get; } = [];

    /// <summary>All probed rows, before the vendor-specific filter is applied.</summary>
    private readonly List<CapabilityEditItem> _all = [];

    /// <summary>Vendor-specific caps (id ≥ 0x8000) are undocumented and hidden unless this is on.</summary>
    [ObservableProperty] public partial bool ShowVendorCaps { get; set; }

    [ObservableProperty] public partial int HiddenVendorCount { get; set; }

    partial void OnShowVendorCapsChanged(bool value) => ApplyFilter();

    [ObservableProperty] public partial int FastDpi { get; set; } = ScanSettings.DefaultFastDpi;
    [ObservableProperty] public partial int HighQualityDpi { get; set; } = ScanSettings.DefaultHighQualityDpi;
    [ObservableProperty] public partial double FoilBrightness { get; set; } = ScanSettings.DefaultFoilBrightness;
    [ObservableProperty] public partial double FoilContrast { get; set; } = ScanSettings.DefaultFoilContrast;

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool HasProbed { get; set; }
    [ObservableProperty] public partial string? StatusMessage { get; set; }

    public void Load()
    {
        RefreshScanners();

        // Preselect whatever is already connected, if anything.
        if (SelectedScanner is null && scannerService.DataSource is not null)
            SelectedScanner = AvailableScanners.FirstOrDefault(
                s => string.Equals(s.Name, scannerService.DataSource.Name, StringComparison.OrdinalIgnoreCase));
    }

    private void RefreshScanners()
    {
        try
        {
            scannerService.EnsureSessionOpen();
            AvailableScanners.Clear();
            foreach (var ds in scannerService.Session.OfType<DataSource>())
                AvailableScanners.Add(ds);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not enumerate scanners");
            StatusMessage = $"Could not list scanners: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RefreshScannerList() => RefreshScanners();

    [RelayCommand]
    private void TestCapabilities()
    {
        if (SelectedScanner is null)
        {
            StatusMessage = "Select a scanner first.";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Connecting to {SelectedScanner.Name} and testing capabilities…";
        try
        {
            // Assigning the DataSource opens/connects it (see ScannerService.OnDataSourceChanged).
            scannerService.DataSource = SelectedScanner;

            var probed = ScannerCapabilityProbe.Probe(
                SelectedScanner, msg => logger.LogDebug("Probe: {Msg}", msg));

            var profile = profileService.GetProfile(SelectedScanner.Name);
            FastDpi = profile.FastDpi ?? ScanSettings.DefaultFastDpi;
            HighQualityDpi = profile.HighQualityDpi ?? ScanSettings.DefaultHighQualityDpi;
            FoilBrightness = profile.FoilBrightness ?? ScanSettings.DefaultFoilBrightness;
            FoilContrast = profile.FoilContrast ?? ScanSettings.DefaultFoilContrast;

            _all.Clear();
            foreach (var cap in probed)
            {
                var saved = profile.Capabilities.FirstOrDefault(c => c.CapId == cap.CapId);
                _all.Add(new CapabilityEditItem(cap, saved?.Value));
            }
            ApplyFilter();

            HasProbed = true;
            var settableCount = _all.Count(c => c.Settable);
            var vendor = _all.Count(c => c.IsVendorSpecific);
            StatusMessage = $"Found {_all.Count} capabilities ({settableCount} settable) on {SelectedScanner.Name}."
                + (vendor > 0 ? $" {vendor} are undocumented vendor-specific settings, hidden by default." : "");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Capability probe failed");
            StatusMessage = $"Could not test capabilities: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Show only standard caps unless the user opts into vendor-specific ones.</summary>
    private void ApplyFilter()
    {
        Capabilities.Clear();
        foreach (var c in _all)
            if (ShowVendorCaps || !c.IsVendorSpecific)
                Capabilities.Add(c);

        HiddenVendorCount = ShowVendorCaps ? 0 : _all.Count(c => c.IsVendorSpecific);
    }

    [RelayCommand]
    private void Save()
    {
        if (SelectedScanner is null) return;

        var profile = profileService.GetProfile(SelectedScanner.Name);
        profile.ScannerName = SelectedScanner.Name;
        profile.FastDpi = FastDpi;
        profile.HighQualityDpi = HighQualityDpi;
        profile.FoilBrightness = FoilBrightness;
        profile.FoilContrast = FoilContrast;
        // Persist only settable caps the user actually changed from the scanner's current value.
        // Iterate the full set (not just visible rows) so a change made while vendor caps were shown
        // isn't lost if they're subsequently hidden.
        profile.Capabilities = _all
            .Where(c => c.Settable && c.IsOverridden)
            .Select(c => c.ToSetting())
            .ToList();

        profileService.SaveProfile(profile);
        StatusMessage = $"Saved {profile.Capabilities.Count} capability override(s) for {SelectedScanner.Name}.";
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        FastDpi = ScanSettings.DefaultFastDpi;
        HighQualityDpi = ScanSettings.DefaultHighQualityDpi;
        FoilBrightness = ScanSettings.DefaultFoilBrightness;
        FoilContrast = ScanSettings.DefaultFoilContrast;
        foreach (var c in _all) c.ResetToDefault();
        StatusMessage = "Reset to scanner defaults (not yet saved).";
    }
}

/// <summary>One editable capability row. Holds the probed metadata plus the user's current choice,
/// exposed by <see cref="Kind"/>-specific properties the view binds to.</summary>
public partial class CapabilityEditItem : ObservableObject
{
    private readonly ProbedCapability _cap;
    private readonly string _currentString;

    public CapabilityEditItem(ProbedCapability cap, string? savedValue)
    {
        _cap = cap;
        _currentString = ToInvariant(cap.Current);

        Options = cap.Options is null ? [] : [.. cap.Options];

        // Seed the editor from the saved override if present, else the scanner's current value.
        var initial = savedValue ?? _currentString;
        switch (cap.Kind)
        {
            case CapKind.Bool:
                BoolValue = ParseBool(initial);
                break;
            case CapKind.Enum:
                SelectedOption = Options.FirstOrDefault(o => o.Value == initial) ?? Options.FirstOrDefault();
                break;
            case CapKind.Range:
                RangeValue = ParseDouble(initial, (double)(cap.RangeMin ?? 0));
                break;
            default:
                TextValue = initial;
                break;
        }
    }

    public string CapId => _cap.CapId;
    public string Label => _cap.Label;
    public string Group => _cap.Group;
    public string Description => _cap.Description;
    public bool HasDescription => !string.IsNullOrEmpty(_cap.Description);
    public CapKind Kind => _cap.Kind;
    public string ItemType => _cap.ItemType;
    public bool Settable => _cap.Settable;
    public bool IsProtected => _cap.Protected;
    public bool IsVendorSpecific => _cap.IsVendorSpecific;

    /// <summary>The scanner's current value, translated to a friendly meaning for enum caps.</summary>
    public string CurrentDisplay => Kind == CapKind.Enum
        ? Options.FirstOrDefault(o => o.Value == _currentString)?.Display ?? _currentString
        : _currentString;

    public double RangeMin => (double)(_cap.RangeMin ?? 0);
    public double RangeMax => (double)(_cap.RangeMax ?? 100);
    public double RangeStep => (double)(_cap.RangeStep is > 0 ? _cap.RangeStep.Value : 1);

    /// <summary>Human-readable valid range for a numeric cap, e.g. "Valid range: -1000 to 1000 (step 1)".</summary>
    public string RangeHint => Kind != CapKind.Range
        ? ""
        : $"Valid range: {Fmt(RangeMin)} to {Fmt(RangeMax)}"
          + (RangeStep > 0 && RangeStep != 1 ? $" (step {Fmt(RangeStep)})" : "");

    public bool HasRangeHint => Kind == CapKind.Range;

    public ObservableCollection<CapValueOption> Options { get; }

    [ObservableProperty] public partial bool BoolValue { get; set; }
    [ObservableProperty] public partial CapValueOption? SelectedOption { get; set; }
    [ObservableProperty] public partial double RangeValue { get; set; }
    [ObservableProperty] public partial string TextValue { get; set; } = "";

    // Visibility helpers for the DataTemplate.
    public bool IsBool => Kind == CapKind.Bool;
    public bool IsEnum => Kind == CapKind.Enum;
    public bool IsRange => Kind == CapKind.Range;
    public bool IsText => Kind == CapKind.Text;

    /// <summary>The user's chosen value as an invariant string.</summary>
    public string ValueString => Kind switch
    {
        CapKind.Bool => BoolValue ? "True" : "False",
        CapKind.Enum => SelectedOption?.Value ?? "",
        CapKind.Range => RangeValue.ToString(CultureInfo.InvariantCulture),
        _ => TextValue ?? "",
    };

    /// <summary>True when the chosen value differs from the scanner's current value.</summary>
    public bool IsOverridden => ValueString != _currentString;

    public ScannerCapabilitySetting ToSetting() => new()
    {
        CapId = CapId,
        ItemType = ItemType,
        Value = ValueString,
    };

    public void ResetToDefault()
    {
        var d = ToInvariant(_cap.Default);
        switch (Kind)
        {
            case CapKind.Bool: BoolValue = ParseBool(d); break;
            case CapKind.Enum: SelectedOption = Options.FirstOrDefault(o => o.Value == d) ?? Options.FirstOrDefault(); break;
            case CapKind.Range: RangeValue = ParseDouble(d, RangeMin); break;
            default: TextValue = d; break;
        }
    }

    private static string ToInvariant(object? o) => o switch
    {
        null => "",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => o.ToString() ?? "",
    };

    private static bool ParseBool(string v)
        => bool.TryParse(v, out var b) ? b
           : int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n != 0;

    private static double ParseDouble(string v, double fallback)
        => double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : fallback;

    private static string Fmt(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
