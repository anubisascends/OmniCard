using System.Net.Http;
using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IDecklistPdfExporter
{
    void Export(DecklistCheckResult result, string filePath);
    void ExportDetailed(DecklistCheckResult result, string filePath, IHttpClientFactory httpClientFactory);
}
