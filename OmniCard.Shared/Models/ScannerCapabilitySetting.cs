namespace OmniCard.Models;

/// <summary>
/// One saved TWAIN capability value for a <see cref="ScannerProfile"/>. String-encoded so the
/// persistence layer (and the dependency-light ScannerHost, into which this file is linked) stay
/// free of any NTwain dependency — the capability applier parses these back into typed values.
/// </summary>
public sealed class ScannerCapabilitySetting
{
    /// <summary>The NTwain <c>CapabilityId</c> name, e.g. <c>"ICapBrightness"</c>.</summary>
    public string CapId { get; set; } = "";

    /// <summary>The TWAIN item-type name (NTwain <c>ItemType</c>), e.g. <c>"Fix32"</c>, <c>"Bool"</c>, <c>"UInt16"</c>.</summary>
    public string ItemType { get; set; } = "";

    /// <summary>The value as an invariant-culture string (bool as <c>"True"</c>/<c>"False"</c>; enums by name or number).</summary>
    public string Value { get; set; } = "";
}
