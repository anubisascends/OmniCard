namespace OmniCard.Models;

/// <summary>
/// Which TWAIN pathway a scanner is reached through. A scanner reachable both ways
/// (e.g. the Canon, which ships both a 64- and 32-bit driver) always resolves to
/// <see cref="InProcess"/> so the working in-process path is never disturbed.
/// </summary>
public enum ScannerOrigin
{
    /// <summary>Reached directly by the 64-bit app (in-process TWAIN).</summary>
    InProcess,

    /// <summary>Reached only via the out-of-process 32-bit (win-x86) scanner helper.</summary>
    X86Host,
}

/// <summary>
/// A discovered TWAIN scanner tagged with the pathway it was found through
/// (see <see cref="ScannerOrigin"/>).
/// </summary>
/// <param name="Name">The TWAIN source's product name, as reported by its driver.</param>
/// <param name="Origin">Which pathway reaches this scanner.</param>
public sealed record ScannerInfo(string Name, ScannerOrigin Origin);
