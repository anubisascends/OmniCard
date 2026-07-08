using OmniCard.Models;

namespace OmniCard.Interfaces;

/// <summary>Exports an AuditReport to a PDF file.</summary>
public interface IAuditPdfExporter
{
    void Export(AuditReport report, string filePath);
}
