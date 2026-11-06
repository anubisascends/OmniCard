namespace OmniCard.Shared;

/// <summary>
/// File-name rules applied identically on every OS. Linux only forbids '/' and NUL, so sanitizing with
/// <see cref="Path.GetInvalidFileNameChars"/> there would keep ':' or '?' — names that break once a data
/// directory or export is moved to (or opened on) Windows. These are Windows' invalid characters.
/// </summary>
public static class PortableFileName
{
    public static readonly char[] InvalidChars =
        [.. Enumerable.Range(0, 32).Select(i => (char)i), '"', '<', '>', '|', ':', '*', '?', '\\', '/'];
}
