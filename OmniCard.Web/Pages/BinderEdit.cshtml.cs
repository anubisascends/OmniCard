using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Web.Services;

namespace OmniCard.Web.Pages;

/// <summary>
/// The editable binder page — a web recreation of the desktop binder view. Unlike the read-only
/// <c>/binder/{id}</c>, this writes to inventory.db (via the API in <see cref="Api.BinderEditController"/>)
/// and is gated behind the <see cref="BinderEditGate"/> passphrase. When locked (or no passphrase is
/// configured) it renders a passphrase prompt instead of the editor.
/// </summary>
public class BinderEditModel : PageModel
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IStorageContainerService _containers;
    private readonly BinderStateBuilder _state;
    private readonly ICardService _cardService;
    private readonly IConfiguration _config;

    public BinderEditModel(
        IStorageContainerService containers,
        BinderStateBuilder state,
        ICardService cardService,
        IConfiguration config)
    {
        _containers = containers;
        _state = state;
        _cardService = cardService;
        _config = config;
    }

    public int Id { get; private set; }
    public StorageContainer Container { get; private set; } = null!;
    public bool EditingEnabled { get; private set; }
    public bool Unlocked { get; private set; }
    public string? LoginError { get; set; }

    public string StateJson { get; private set; } = "null";
    public string UnplacedJson { get; private set; } = "[]";
    public string GamesJson { get; private set; } = "[]";

    public IActionResult OnGet(int id)
    {
        if (!Load(id))
            return NotFound();
        return Page();
    }

    public IActionResult OnPost(int id, string? passphrase)
    {
        if (!Load(id))
            return NotFound();

        if (BinderEditGate.Verify(_config, passphrase))
        {
            BinderEditGate.Unlock(HttpContext);
            return RedirectToPage(new { id });
        }

        LoginError = "Incorrect passphrase.";
        return Page();
    }

    private bool Load(int id)
    {
        var container = _containers.GetAll().FirstOrDefault(c => c.Id == id);
        if (container is null)
            return false;

        Id = id;
        Container = container;
        EditingEnabled = BinderEditGate.IsEnabled(_config);
        Unlocked = EditingEnabled && BinderEditGate.IsUnlocked(HttpContext);

        if (Unlocked)
        {
            StateJson = JsonSerializer.Serialize(_state.BuildState(id, 0), JsonOptions);
            UnplacedJson = JsonSerializer.Serialize(_state.BuildUnplaced(id, null), JsonOptions);
            GamesJson = JsonSerializer.Serialize(
                _cardService.AvailableGames.Select(g => new { id = (int)g, name = g.ToString() }), JsonOptions);
        }

        return true;
    }
}
