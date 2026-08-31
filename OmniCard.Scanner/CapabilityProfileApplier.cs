using System.Globalization;
using System.Reflection;
using NTwain.Data;
using OmniCard.Models;

namespace OmniCard.Scanner;

/// <summary>
/// Applies a per-scanner profile's arbitrary TWAIN capabilities on top of OmniCard's baseline
/// (<see cref="ScanSettingsApplier"/>). This is the "layered override" step: after the baseline
/// guarantees a working scan, the user's saved caps are set.
///
/// Setting goes through NTwain's own strongly-typed capability wrapper for each cap (the wrapper
/// property whose name matches the <see cref="CapabilityId"/>), reached by reflection — so NTwain
/// performs the correct per-item-type container marshaling instead of us hand-packing TWAIN
/// structures. Every write is guarded (CanSet + try/catch) and never faults the scan.
///
/// This file is linked into the dependency-light ScannerHost, so it must stay logging/EF-free
/// (progress goes to an optional <c>onDebug</c> callback) and depend only on NTwain + the plain
/// <see cref="ScannerCapabilitySetting"/> model.
/// </summary>
public static class CapabilityProfileApplier
{
    /// <summary>
    /// Capabilities OmniCard manages itself for reliable card matching / image transfer. These are
    /// never applied from a user profile and are shown read-only in the UI — changing them (e.g.
    /// pixel type away from RGB, or the transfer mechanism) would silently break scanning/matching.
    /// </summary>
    public static readonly IReadOnlySet<CapabilityId> CriticalCaps = new HashSet<CapabilityId>
    {
        CapabilityId.ICapPixelType,
        CapabilityId.ICapXferMech,
        CapabilityId.ICapCompression,
        CapabilityId.ICapImageFileFormat,
        CapabilityId.ICapICCProfile,
        CapabilityId.ICapPlanarChunky,
        CapabilityId.CapXferCount,
        CapabilityId.CapAutoScan,
        CapabilityId.CapDuplex,
        CapabilityId.CapDuplexEnabled,
        CapabilityId.CapFeederEnabled,
    };

    public static void Apply(
        NTwain.DataSource ds,
        IEnumerable<ScannerCapabilitySetting>? settings,
        Action<string>? onDebug = null)
    {
        if (settings is null) return;

        object caps = ds.Capabilities;
        var capsType = caps.GetType();

        foreach (var setting in settings)
        {
            if (string.IsNullOrWhiteSpace(setting.CapId)) continue;

            if (!Enum.TryParse<CapabilityId>(setting.CapId, ignoreCase: false, out var capId))
            {
                onDebug?.Invoke($"Unknown capability '{setting.CapId}', skipping");
                continue;
            }

            if (CriticalCaps.Contains(capId))
            {
                onDebug?.Invoke($"'{setting.CapId}' is managed by OmniCard, skipping");
                continue;
            }

            try
            {
                ApplyOne(caps, capsType, capId, setting, onDebug);
            }
            catch (Exception ex)
            {
                onDebug?.Invoke($"Cannot set '{setting.CapId}' = '{setting.Value}': {ex.Message}");
            }
        }
    }

    private static void ApplyOne(
        object caps,
        Type capsType,
        CapabilityId capId,
        ScannerCapabilitySetting setting,
        Action<string>? onDebug)
    {
        // The wrapper property is named exactly like the CapabilityId (e.g. "ICapBrightness").
        var prop = capsType.GetProperty(capId.ToString(), BindingFlags.Public | BindingFlags.Instance);
        var wrapper = prop?.GetValue(caps);
        if (wrapper is null)
        {
            onDebug?.Invoke($"No wrapper for '{capId}', skipping");
            return;
        }

        var wrapperType = wrapper.GetType();

        if (wrapperType.GetProperty("CanSet")?.GetValue(wrapper) is bool canSet && !canSet)
        {
            onDebug?.Invoke($"'{capId}' is not settable on this scanner, skipping");
            return;
        }

        // CapWrapper<T> — T is the capability's value type; convert our stored string into it.
        var valueType = wrapperType.IsGenericType ? wrapperType.GetGenericArguments()[0] : null;
        if (valueType is null)
        {
            onDebug?.Invoke($"'{capId}' wrapper is not generic, skipping");
            return;
        }

        object typed = ConvertValue(setting.Value, valueType);

        // Pick SetValue(T) specifically (not the TWArray/TWEnumeration overloads).
        var setMethod = wrapperType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "SetValue"
                                 && m.GetParameters().Length == 1
                                 && m.GetParameters()[0].ParameterType == valueType);
        if (setMethod is null)
        {
            onDebug?.Invoke($"No SetValue({valueType.Name}) for '{capId}', skipping");
            return;
        }

        setMethod.Invoke(wrapper, [typed]);
        onDebug?.Invoke($"Set '{capId}' = '{setting.Value}'");
    }

    /// <summary>Convert a stored invariant string into the capability wrapper's value type.</summary>
    private static object ConvertValue(string value, Type targetType)
    {
        if (targetType == typeof(string)) return value;
        if (targetType == typeof(BoolType)) return ParseBool(value) ? BoolType.True : BoolType.False;
        if (targetType == typeof(TWFix32)) return (TWFix32)float.Parse(value, CultureInfo.InvariantCulture);
        if (targetType.IsEnum) return Enum.Parse(targetType, value, ignoreCase: true);
        return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }

    private static bool ParseBool(string v)
        => bool.TryParse(v, out var b)
            ? b
            : int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n != 0;
}
