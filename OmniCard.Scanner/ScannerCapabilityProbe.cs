using System.Globalization;
using NTwain;
using NTwain.Data;

namespace OmniCard.Scanner;

/// <summary>
/// Probes every capability a connected TWAIN source advertises, reading each one's kind, allowed
/// values, current/default, and whether it can be set — so the Settings UI can render a dynamic,
/// per-scanner editor. Reads via NTwain's generic by-<see cref="CapabilityId"/> surface on the
/// concrete <see cref="Capabilities"/>. Runs only in-process (the app), never in the host.
/// </summary>
public static class ScannerCapabilityProbe
{
    public static IReadOnlyList<ProbedCapability> Probe(DataSource ds, Action<string>? onDebug = null)
    {
        var results = new List<ProbedCapability>();

        // The generic (by-id) capability API lives on the concrete Capabilities type.
        if (ds.Capabilities is not Capabilities caps)
        {
            onDebug?.Invoke("Generic capability access is unavailable for this source");
            return results;
        }

        IEnumerable<CapabilityId> supported;
        try
        {
            supported = caps.CapSupportedCaps.GetValues() ?? [];
        }
        catch (Exception ex)
        {
            onDebug?.Invoke($"Could not read the list of supported capabilities: {ex.Message}");
            return results;
        }

        object capsObj = caps;
        var capsType = capsObj.GetType();

        foreach (var cap in supported.Distinct())
        {
            try
            {
                var probed = ProbeOne(caps, capsObj, capsType, cap);
                if (probed is not null) results.Add(probed);
            }
            catch (Exception ex)
            {
                onDebug?.Invoke($"Skipped capability '{cap}': {ex.Message}");
            }
        }

        return results.OrderBy(p => p.Group, StringComparer.Ordinal)
                      .ThenBy(p => p.Label, StringComparer.Ordinal)
                      .ToList();
    }

    private static ProbedCapability? ProbeOne(Capabilities caps, object capsObj, Type capsType, CapabilityId cap)
    {
        CapabilityReader reader;
        try { reader = caps.GetValuesRaw(cap); }
        catch { return null; }

        object? current = TryGet(() => caps.GetCurrent(cap));
        object? def = TryGet(() => caps.GetDefault(cap));

        CapKind kind;
        IReadOnlyList<CapValueOption>? options = null;
        decimal? min = null, max = null, step = null;

        if (reader.ItemType == ItemType.Bool)
        {
            kind = CapKind.Bool;
        }
        else if (reader.ContainerType == ContainerType.Range)
        {
            kind = CapKind.Range;
            min = ToDec(reader.RangeMinValue);
            max = ToDec(reader.RangeMaxValue);
            step = ToDec(reader.RangeStepSize);
        }
        else if (reader.ContainerType is ContainerType.Enum or ContainerType.Array)
        {
            var raw = reader.EnumerateCapValues().ToList();
            if (raw.Count > 0)
            {
                options = raw.Select(o => BuildOption(cap, o)).ToList();
                kind = CapKind.Enum;
            }
            else
            {
                kind = CapKind.Text;
            }
        }
        else
        {
            kind = CapKind.Text; // single value (numeric or string), free entry
        }

        bool critical = CapabilityProfileApplier.CriticalCaps.Contains(cap);
        bool settable = !critical && IsSettable(caps, capsObj, capsType, cap);

        return new ProbedCapability
        {
            Cap = cap,
            CapId = cap.ToString(),
            Label = CapabilityLabels.LabelFor(cap),
            Group = CapabilityLabels.GroupFor(cap),
            Description = CapabilityLabels.DescriptionFor(cap),
            Kind = kind,
            ItemType = reader.ItemType.ToString(),
            Current = current,
            Default = def,
            Options = options,
            RangeMin = min,
            RangeMax = max,
            RangeStep = step,
            Settable = settable,
            Protected = critical,
            IsVendorSpecific = CapabilityLabels.IsCustom(cap),
        };
    }

    private static bool IsSettable(Capabilities caps, object capsObj, Type capsType, CapabilityId cap)
    {
        try
        {
            var q = caps.QuerySupport(cap);
            if (q.HasValue) return q.Value.HasFlag(QuerySupports.Set);
        }
        catch { /* fall through to reflection fallback */ }

        // Some sources don't implement QuerySupport — fall back to the named wrapper's CanSet.
        try
        {
            var wrapper = capsType.GetProperty(cap.ToString())?.GetValue(capsObj);
            if (wrapper?.GetType().GetProperty("CanSet")?.GetValue(wrapper) is bool canSet)
                return canSet;
        }
        catch { /* ignore */ }

        return false;
    }

    private static CapValueOption BuildOption(CapabilityId cap, object raw)
    {
        var value = ToInvariant(raw);
        var meaning = TryToLong(raw) is long code ? CapabilityValueMeanings.Describe(cap.ToString(), code) : null;
        // Prefer the spec meaning; else the raw text (already a name for NTwain enums, a number otherwise).
        return new CapValueOption { Value = value, Display = meaning ?? value };
    }

    private static object? TryGet(Func<object?> get)
    {
        try { return get(); }
        catch { return null; }
    }

    private static string ToInvariant(object? o) => o switch
    {
        null => "",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => o.ToString() ?? "",
    };

    private static long? TryToLong(object? o)
    {
        try { return o is null ? null : Convert.ToInt64(o, CultureInfo.InvariantCulture); }
        catch { return null; }
    }

    private static decimal? ToDec(object? o) => o switch
    {
        null => null,
        TWFix32 f => f.Whole, // integer part is enough for a UI range hint
        sbyte or byte or short or ushort or int or uint or long or ulong
            => Convert.ToDecimal(o, CultureInfo.InvariantCulture),
        _ => decimal.TryParse(o.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null,
    };
}
