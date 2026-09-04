using Microsoft.AspNetCore.Mvc;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Web.Services;

namespace OmniCard.Web.Api;

/// <summary>CSV collection import via file upload. Parses with the shared
/// <see cref="ICsvExportImportService"/> (auto-detects app-native / TCGplayer / Moxfield / Manabox),
/// then writes the rows as new lots via <see cref="WebBinderCardService"/>.</summary>
public sealed class ImportController(
    ICsvExportImportService csv,
    WebBinderCardService binderCards) : ApiControllerBase
{
    [HttpPost("csv")]
    public IActionResult Csv(
        IFormFile file,
        [FromQuery] bool skipDuplicates = true,
        [FromQuery] int? targetContainerId = null)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded." });

        var path = Path.Combine(Path.GetTempPath(), $"omnicard-import-{Guid.NewGuid():N}.csv");
        try
        {
            using (var fs = System.IO.File.Create(path))
                file.CopyTo(fs);

            var preview = csv.PreviewImport(path);
            if (preview.Cards.Count == 0)
                return BadRequest(new { error = "No importable rows found.", warnings = preview.Warnings });

            foreach (var card in preview.Cards)
            {
                // Foil finish default (mirrors CsvExportImportService.ImportCards).
                if (!card.IsFoil) card.FoilType = null;
                else if (string.IsNullOrEmpty(card.FoilType)) card.FoilType = FoilTypes.BasicFoilType(card.Game);

                if (targetContainerId is not null && card.ContainerId is null)
                {
                    card.ContainerId = targetContainerId.Value;
                    card.Container = null;
                }
            }

            var imported = binderCards.ImportCollectionCards(preview.Cards, skipDuplicates);
            return Ok(new
            {
                imported,
                totalRows = preview.TotalRows,
                detectedFormat = preview.DetectedFormat.ToString(),
                warnings = preview.Warnings,
            });
        }
        finally
        {
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
    }
}
