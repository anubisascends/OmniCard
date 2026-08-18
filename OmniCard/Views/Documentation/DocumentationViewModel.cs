using System.IO;

namespace OmniCard.Views.Documentation;

/// <summary>
/// Backs the Documentation dialog. Resolves the on-disk location of the bundled HTML help
/// (copied next to the executable under <c>Docs/</c> — see the .csproj Content include) so the
/// view can serve it to a WebView2 via a virtual host mapping.
/// </summary>
public sealed class DocumentationViewModel : ViewModel
{
    /// <summary>Virtual host the docs folder is mapped to, e.g. https://omnicard.help/index.html.</summary>
    public const string VirtualHost = "omnicard.help";

    /// <summary>Absolute path to the bundled docs folder, or null if it isn't present.</summary>
    public string? DocsFolder { get; }

    /// <summary>The start page URL under the virtual host.</summary>
    public string StartUrl => $"https://{VirtualHost}/index.html";

    /// <summary>True when the docs folder exists and the WebView2 can be pointed at it.</summary>
    public bool DocsAvailable => DocsFolder is not null;

    public DocumentationViewModel()
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, "Docs");
        if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "index.html")))
            DocsFolder = candidate;
    }
}
