using OmniCard.Shared.Scanning;

namespace OmniCard.Shared.Audit;

/// <summary>Exports diagnostic scan events to a formatted text file.</summary>
public interface IDiagnosticExporter
{
    string Render(List<ScanDiagnosticEvent> events);
}
