using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Models;
// "Card" is ambiguous with the sibling OmniCard.Views.Card namespace when resolved from
// OmniCard.Views.Root — alias to the Scryfall model explicitly.
using MtgCard = OmniCard.Models.Card;

namespace OmniCard.Views.Root;

public sealed partial class CollectionViewModel
{
    /// <summary>One readonly row in the card-details sidebar.</summary>
    public sealed record SidebarField(string Label, string Value);

    /// <summary>Above this many selected cards, skip the per-card catalog DB lookups (one
    /// synchronous query per card) so a huge multi-select doesn't freeze the UI. Full catalog
    /// data across dozens of differing cards rarely stays common enough to show anyway.</summary>
    private const int MaxSelectionForCatalogLookup = 20;

    private static readonly JsonSerializerOptions ExtendedDataJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record TcgCsvExtendedDataEntry(string? Name, string? DisplayName, string? Value);

    /// <summary>Fields common to every selected card, rebuilt on every selection change. A field
    /// is included only when all selected cards share the same value for it — differing values
    /// hide the row entirely rather than showing a placeholder. Empty (just the panel title shows)
    /// when nothing is selected — the panel itself stays visible regardless of selection.</summary>
    public ObservableCollection<SidebarField> SidebarFields { get; } = [];

    /// <summary>Minimum usable width (px) for the details panel.</summary>
    public const double MinSidebarWidth = 220;

    private double _sidebarWidth = MinSidebarWidth;

    /// <summary>Details panel width (px), persisted. Clamped to <see cref="MinSidebarWidth"/>.</summary>
    public double SidebarWidth
    {
        get => _sidebarWidth;
        set
        {
            var clamped = value < MinSidebarWidth ? MinSidebarWidth : value;
            if (clamped == _sidebarWidth) return;
            _sidebarWidth = clamped;
            OnPropertyChanged();
            PersistSettings?.Invoke();
        }
    }

    /// <summary>Whether the user has collapsed the details panel. Persisted.</summary>
    [ObservableProperty]
    public partial bool IsSidebarCollapsed { get; set; }

    partial void OnIsSidebarCollapsedChanged(bool value) => PersistSettings?.Invoke();

    [RelayCommand]
    private void CollapseSidebar() => IsSidebarCollapsed = true;

    [RelayCommand]
    private void ExpandSidebar() => IsSidebarCollapsed = false;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ClearSelectionAction() => ClearSelection?.Invoke();

    /// <summary>Called by the View on every ListBox SelectionChanged — not driven off
    /// SelectedCardCount, which doesn't change (and so doesn't fire) when swapping from one
    /// single-card selection to another single-card selection.</summary>
    public void RebuildSidebarFields()
    {
        SidebarFields.Clear();

        var selected = GetSelectedCards?.Invoke();
        if (selected is null || selected.Count == 0) return;

        void AddIfCommon(string label, Func<CollectionCard, string> value, bool hideIfBlank = false)
        {
            var first = value(selected[0]);
            if (selected.Any(c => value(c) != first)) return;
            if (hideIfBlank && string.IsNullOrEmpty(first)) return;
            SidebarFields.Add(new SidebarField(label, first));
        }

        string FormatLocation(CollectionCard c)
        {
            if (c.ContainerId is null) return "";
            var name = AvailableContainers.FirstOrDefault(x => x.Id == c.ContainerId)?.Name ?? "";
            var parts = new List<string>();
            if (c.Page is not null) parts.Add($"Page {c.Page}");
            if (c.Slot is not null) parts.Add($"Slot {c.Slot}");
            if (!string.IsNullOrEmpty(c.Section)) parts.Add(c.Section!);
            return parts.Count > 0 ? $"{name} ({string.Join(", ", parts)})" : name;
        }

        string FormatMarketPrice(CollectionCard c) =>
            (MarketPrices.TryGetValue(c.Id, out var price) ? price : c.MarketPrice).ToString("C2");

        // Identity/pricing fields: shown whenever common, even at their default/blank value.
        AddIfCommon("Name", c => c.Name);
        AddIfCommon("Game", c => c.Game.ToString());
        AddIfCommon("Set", c => $"{c.SetName} ({c.SetCode})");
        AddIfCommon("Number", c => c.Number);
        AddIfCommon("Rarity", c => c.Rarity);
        AddIfCommon("Condition", c => c.Condition);
        AddIfCommon("Foil", c => c.IsFoil ? "Yes" : "No");
        AddIfCommon("Market Price", FormatMarketPrice);
        AddIfCommon("Date Added", c => c.DateAdded.ToLocalTime().ToString("d"));

        // Optional/conditional fields: shown only when common and non-blank.
        AddIfCommon("Color", c => c.Color ?? "", hideIfBlank: true);
        AddIfCommon("Card Type", c => c.CardType ?? "", hideIfBlank: true);
        AddIfCommon("Purchase Price", c => c.PurchasePrice?.ToString("C2") ?? "", hideIfBlank: true);
        AddIfCommon("Location", FormatLocation, hideIfBlank: true);
        AddIfCommon("Tags", c => string.Join(", ", c.Tags.OrderBy(t => t)), hideIfBlank: true);
        AddIfCommon("Listing Status", c => c.ListingStatus?.ToString() ?? "", hideIfBlank: true);
        AddIfCommon("eBay Status", c => c.EbayListing?.Status.ToString() ?? "", hideIfBlank: true);
        AddIfCommon("Traded", c => c.IsTraded ? "Yes" : "", hideIfBlank: true);
        AddIfCommon("Trade Note", c => c.TradeNote ?? "", hideIfBlank: true);
        AddIfCommon("Missing", c => c.IsMissing ? "Yes" : "", hideIfBlank: true);
        AddIfCommon("Flag Reason", c => c.FlagReason is null or FlagReason.None ? "" : c.FlagReason.ToString() ?? "", hideIfBlank: true);

        // Quantity is derived from stack size, not compared for equality — only meaningful when
        // a single stacked tile is selected (per-tile QTY, not a sum across a multi-selection).
        if (selected.Count == 1 && selected[0].Quantity > 1)
            SidebarFields.Add(new SidebarField("Quantity", selected[0].Quantity.ToString()));

        if (selected.Count <= MaxSelectionForCatalogLookup)
            AddCatalogFields(selected, AddIfCommon);
    }

    /// <summary>Adds fields from each game's own card database (Scryfall/OPTCG/Riftbound/TCGCSV),
    /// on top of the inventory-level fields above. Every game exposes catalog lookup the same way
    /// (<see cref="OmniCard.Interfaces.ICardService.GetGameService"/> + FindCardById), but the
    /// returned shape differs per game, so field extraction is a per-game switch.</summary>
    private void AddCatalogFields(IList<CollectionCard> selected, Action<string, Func<CollectionCard, string>, bool> addIfCommon)
    {
        // One DB lookup per selected card, cached by CollectionCard.Id so the per-field selectors
        // below don't re-query for every field.
        var catalog = new Dictionary<int, object?>();
        foreach (var c in selected)
        {
            try { catalog[c.Id] = _cardService.GetGameService(c.Game).FindCardById(c.GameCardId); }
            catch { catalog[c.Id] = null; }
        }

        string S(CollectionCard c, Func<MtgCard, string?> get) => catalog[c.Id] is MtgCard card ? get(card) ?? "" : "";
        string O(CollectionCard c, Func<OptcgCard, string?> get) => catalog[c.Id] is OptcgCard card ? get(card) ?? "" : "";
        string R(CollectionCard c, Func<RiftboundCard, string?> get) => catalog[c.Id] is RiftboundCard card ? get(card) ?? "" : "";

        // Magic (Scryfall)
        addIfCommon("Mana Cost", c => S(c, x => x.ManaCost), true);
        addIfCommon("Type Line", c => S(c, x => x.TypeLine), true);
        addIfCommon("Oracle Text", c => S(c, x => x.OracleText), true);
        addIfCommon("Power/Toughness", c => catalog[c.Id] is MtgCard mc && (mc.Power is not null || mc.Toughness is not null) ? $"{mc.Power}/{mc.Toughness}" : "", true);
        addIfCommon("Loyalty", c => S(c, x => x.Loyalty), true);
        addIfCommon("Defense", c => S(c, x => x.Defense), true);
        addIfCommon("Colors", c => catalog[c.Id] is MtgCard mc && mc.Colors is { Count: > 0 } ? string.Join(", ", mc.Colors) : "", true);
        addIfCommon("Color Identity", c => catalog[c.Id] is MtgCard mc && mc.ColorIdentity.Count > 0 ? string.Join(", ", mc.ColorIdentity) : "", true);
        addIfCommon("Keywords", c => catalog[c.Id] is MtgCard mc && mc.Keywords.Count > 0 ? string.Join(", ", mc.Keywords) : "", true);
        addIfCommon("Flavor Text", c => S(c, x => x.FlavorText), true);
        addIfCommon("Watermark", c => S(c, x => x.Watermark), true);
        addIfCommon("Legalities", c => catalog[c.Id] is MtgCard mc && mc.Legalities.Count > 0
            ? string.Join(", ", mc.Legalities.Where(kv => kv.Value != "not_legal").Select(kv => $"{kv.Key}: {kv.Value}"))
            : "", true);

        // One Piece (OPTCG)
        addIfCommon("Cost", c => O(c, x => x.CardCost), true);
        addIfCommon("Power", c => O(c, x => x.CardPower), true);
        addIfCommon("Life", c => O(c, x => x.Life), true);
        addIfCommon("Counter", c => catalog[c.Id] is OptcgCard oc && oc.CounterAmount is not null ? oc.CounterAmount.ToString()! : "", true);
        addIfCommon("Attribute", c => O(c, x => x.Attribute), true);
        addIfCommon("Sub Types", c => O(c, x => x.SubTypes), true);

        // Shared across OPTCG/Riftbound
        addIfCommon("Card Text", c => catalog[c.Id] switch { OptcgCard oc => oc.CardText ?? "", RiftboundCard rc => rc.CardText ?? "", _ => "" }, true);
        addIfCommon("Artist", c => catalog[c.Id] switch { MtgCard mc => mc.Artist ?? "", OptcgCard oc => oc.Artist ?? "", RiftboundCard rc => rc.Artist ?? "", _ => "" }, true);

        // Riftbound
        addIfCommon("Supertype", c => R(c, x => x.Supertype), true);
        addIfCommon("Domain", c => R(c, x => x.Domain), true);
        addIfCommon("Energy", c => catalog[c.Id] is RiftboundCard rc && rc.Energy is not null ? rc.Energy.ToString()! : "", true);
        addIfCommon("Might", c => catalog[c.Id] is RiftboundCard rc2 && rc2.Might is not null ? rc2.Might.ToString()! : "", true);
        addIfCommon("Flavour", c => R(c, x => x.Flavour), true);

        // Pokemon/Yu-Gi-Oh!/FFTCG (TcgCsvCard) — every game-specific attribute (HP, attacks, ATK/DEF,
        // level, job, cost, etc.) lives as a generic {name, displayName, value} array rather than
        // strongly-typed properties. Surface every entry directly instead of hardcoding field names
        // per game. "Number"/"Rarity" are skipped since they're already shown above.
        var extendedDataByCard = new Dictionary<int, Dictionary<string, string>>();
        foreach (var c in selected)
        {
            if (catalog[c.Id] is TcgCsvCard tc)
                extendedDataByCard[c.Id] = ParseExtendedData(tc.ExtendedDataJson);
        }

        var extendedLabels = extendedDataByCard.Values
            .SelectMany(m => m.Keys)
            .Where(l => l is not ("Number" or "Rarity"))
            .Distinct()
            .OrderBy(l => l);

        foreach (var label in extendedLabels)
        {
            var lbl = label;
            addIfCommon(lbl, c => extendedDataByCard.TryGetValue(c.Id, out var map) && map.TryGetValue(lbl, out var v) ? v : "", true);
        }
    }

    private static Dictionary<string, string> ParseExtendedData(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var entries = JsonSerializer.Deserialize<List<TcgCsvExtendedDataEntry>>(json, ExtendedDataJsonOptions);
            if (entries is null) return [];

            var map = new Dictionary<string, string>();
            foreach (var entry in entries)
            {
                var label = entry.DisplayName ?? entry.Name;
                if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(entry.Value)) continue;
                map[label] = entry.Value;
            }
            return map;
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
