using System.Text;

namespace OmniCard.Shared.Sales;

/// <summary>
/// Normalizes and compares shipping tracking numbers against what a barcode scanner reads off a label.
/// A label's barcode often isn't the bare tracking number: USPS IMpb barcodes prefix it with
/// <c>420</c> + the destination ZIP, and FedEx's long 1D barcodes carry the tracking number at the end
/// of a 22/34-digit payload. Matching is therefore "equal, or one ends with the other" over normalized
/// values (upper-case alphanumerics only, so spaces, dashes, GS1 parentheses and FNC1 separators drop out).
/// </summary>
public static class TrackingNumbers
{
    /// <summary>Shortest stored value that may match by suffix — keeps a short or partial number from
    /// matching the tail of an unrelated barcode.</summary>
    public const int MinSuffixLength = 10;

    /// <summary>Upper-case alphanumerics only; empty for null/blank input.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
            if (char.IsAsciiLetterOrDigit(c))
                sb.Append(char.ToUpperInvariant(c));
        return sb.ToString();
    }

    /// <summary>The tracking number carried by a scanned label barcode (normalized): strips the USPS
    /// <c>420</c>+ZIP routing prefix and reduces FedEx's long barcodes to the tracking number. Anything
    /// unrecognized is returned normalized but otherwise as scanned.</summary>
    public static string Extract(string? scanned)
    {
        var code = Normalize(scanned);
        if (code.Length == 0 || !code.All(char.IsAsciiDigit)) return code;

        // USPS IMpb: "420" + ZIP5 or ZIP9 + tracking (20+ digits, starting with 9).
        if (code.StartsWith("420"))
        {
            var rest = code[3..];
            if (rest.Length - 9 >= 20 && rest[9] == '9') return rest[9..];
            if (rest.Length - 5 >= 20 && rest[5] == '9') return rest[5..];
            return code;
        }

        // FedEx Ground "96" barcode (22 digits) → last 15; FedEx Express 34-digit barcode → last 12.
        if (code.Length == 22 && code.StartsWith("96")) return code[^15..];
        if (code.Length == 34) return code[^12..];
        return code;
    }

    /// <summary>True when a stored tracking number and a scanned barcode refer to the same package.</summary>
    public static bool Matches(string? stored, string? scanned)
    {
        var a = Normalize(stored);
        var b = Normalize(scanned);
        if (a.Length == 0 || b.Length == 0) return false;
        if (a == b) return true;
        // The barcode carries extra routing data around the tracking number (or the stored value is the
        // raw barcode and the scan is the bare number) — accept a suffix match of a long-enough value.
        return (b.EndsWith(a, StringComparison.Ordinal) && a.Length >= MinSuffixLength)
            || (a.EndsWith(b, StringComparison.Ordinal) && b.Length >= MinSuffixLength)
            || (Extract(a) is { Length: > 0 } ea && ea == Extract(b));
    }
}
