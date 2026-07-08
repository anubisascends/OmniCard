using OmniCard.Models;

namespace OmniCard.Interfaces;

/// <summary>Exports diagnostic scan events to a formatted text file.</summary>
public interface IDiagnosticExporter
{
    string Render(List<ScanDiagnosticEvent> events);
}
