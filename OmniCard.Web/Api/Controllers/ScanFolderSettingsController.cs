using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;
using OmniCard.Shared.Settings;
using OmniCard.Web.Api.Infrastructure;
using OmniCard.Web.Services.ScanBatches;

namespace OmniCard.Web.Api.Controllers;

/// <summary>Watched scan folder configuration (Settings ▸ Scan). Admin only: these settings point the
/// server at arbitrary paths and move files out of them.</summary>
[ApiController]
[ApiAuth(RequireAdmin = true)]
[Route("api/settings/scan-folders")]
public sealed class ScanFolderSettingsController(
    IScanFolderSettingsService settings,
    ScanFolderIngestor ingestor,
    IEnumerable<ICardGameService> gameServices) : ControllerBase
{
    [HttpGet]
    public ActionResult<ScanFolderSettingsResponse> Get()
    {
        var s = settings.Get();
        return new ScanFolderSettingsResponse(ToDto(s), s.Folders.Select(Status).ToList());
    }

    [HttpPut]
    public IActionResult Update([FromBody] ScanFolderSettingsDto request)
    {
        var available = gameServices.Select(g => g.Game).ToHashSet();
        var errors = new List<string>();
        var folders = new List<ScanFolderConfig>();
        foreach (var f in request.Folders)
        {
            if (LocationsController.ParseGame(f.Game) is not { } game || !available.Contains(game))
            {
                errors.Add($"Unknown game '{f.Game}'.");
                continue;
            }
            folders.Add(new ScanFolderConfig
            {
                Game = game,
                Path = f.Path ?? "",
                Enabled = f.Enabled,
                IsFoil = f.IsFoil,
                Condition = f.Condition,
                Language = f.Language,
                SetCodes = [.. f.SetCodes],
                DefaultContainerId = f.DefaultContainerId,
            });
        }
        if (errors.Count == 0)
        {
            errors.AddRange(settings.Save(new ScanFolderSettings
            {
                Enabled = request.Enabled,
                QuietPeriodSeconds = request.QuietPeriodSeconds,
                RetentionDays = request.RetentionDays,
                Folders = folders,
            }));
        }
        return errors.Count == 0 ? NoContent() : BadRequest(new { error = string.Join(" ", errors), errors });
    }

    private static ScanFolderSettingsDto ToDto(ScanFolderSettings s) => new()
    {
        Enabled = s.Enabled,
        QuietPeriodSeconds = s.QuietPeriodSeconds,
        RetentionDays = s.RetentionDays,
        Folders = s.Folders.Select(f => new ScanFolderConfigDto
        {
            Game = f.Game.ToString(),
            Path = f.Path,
            Enabled = f.Enabled,
            IsFoil = f.IsFoil,
            Condition = f.Condition,
            Language = f.Language,
            SetCodes = f.SetCodes,
            DefaultContainerId = f.DefaultContainerId,
        }).ToList(),
    };

    private ScanFolderStatusDto Status(ScanFolderConfig f)
    {
        var exists = Directory.Exists(f.Path);
        return new ScanFolderStatusDto(f.Game.ToString(), exists, exists && CanWrite(f.Path),
            ingestor.FolderErrors.GetValueOrDefault(f.Game));
    }

    /// <summary>Whether the app can create files under the folder's <c>_processed</c> subfolder (where
    /// picked-up originals are moved).</summary>
    private static bool CanWrite(string root)
    {
        try
        {
            var processed = Path.Combine(root, ScanFolderIngestor.ProcessedFolderName);
            Directory.CreateDirectory(processed);
            var probe = Path.Combine(processed, $".omnicard-write-test-{Guid.NewGuid():N}");
            System.IO.File.WriteAllText(probe, "");
            System.IO.File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
