using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using OmniCard.Helpers;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.StorageManager;

public sealed partial class StorageManagerViewModel(
    IStorageContainerService containerService,
    IOptionsMonitor<WebCompanionSettings> webCompanionSettings,
    IDataPathService dataPathService) : ViewModel
{
    // The editable container types shown as groups, in display order. The system Bulk location is
    // deliberately excluded — it can't be edited, added to, renamed, or deleted.
    private static readonly (ContainerType Type, string Display)[] EditableGroups =
    [
        (ContainerType.Binder, "Binder"),
        (ContainerType.Box, "Box"),
        (ContainerType.DeckBox, "Deck Box"),
        (ContainerType.DisplayCase, "Display Case"),
    ];

    public ObservableCollection<LocationGroupViewModel> Groups { get; } = [];

    // Every existing container name (including the system Bulk location), lower-cased, for live
    // uniqueness validation in the inline add/rename fields. Rebuilt on each Load().
    private readonly HashSet<string> _allNames = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    public partial string BaseUrl { get; set; } = "";

    public Action? CloseDialog { get; set; }

    public void Load()
    {
        BaseUrl = webCompanionSettings.CurrentValue.BaseUrl;

        var all = containerService.GetAll();
        _allNames.Clear();
        foreach (var c in all)
            _allNames.Add(c.Name);

        // Build the four editable groups once (preserving expander/typing state across reloads),
        // then refill each with its (non-system) containers.
        if (Groups.Count == 0)
        {
            var expanded = StorageManagerUiState.LoadExpandedState(dataPathService.DataDirectory);
            foreach (var (type, display) in EditableGroups)
            {
                var isExpanded = expanded.GetValueOrDefault(type.ToString(), true);
                Groups.Add(new LocationGroupViewModel(this, type, display, isExpanded));
            }
        }

        foreach (var group in Groups)
        {
            group.Items.Clear();
            foreach (var c in all
                         .Where(c => !c.IsSystem && c.ContainerType == group.ContainerType)
                         .OrderBy(c => c.Name))
            {
                group.Items.Add(new ContainerDisplayItem
                {
                    Id = c.Id,
                    Name = c.Name,
                    ContainerType = c.ContainerType,
                    IsSystem = c.IsSystem,
                    CardCount = containerService.GetCardCount(c.Id),
                    ExcludeFromDeckCheck = c.ExcludeFromDeckCheck,
                    AlwaysAvailable = c.AlwaysAvailable,
                });
            }
            group.RevalidateNewName();
        }
    }

    /// <summary>True if <paramref name="name"/> (trimmed, case-insensitive) is already taken by any
    /// container, including the reserved system Bulk location. <paramref name="excludeId"/> ignores
    /// one container (for rename-to-same-name).</summary>
    public bool NameInUse(string name, int? excludeId = null)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) return false;
        if (excludeId is null)
            return _allNames.Contains(trimmed);

        // Exclude the container being renamed by comparing against every other name.
        return containerService.NameExists(trimmed, excludeId);
    }

    public void AddLocation(ContainerType type, string name)
    {
        // Binders default to 9 slots/page (adjustable later in the binder view); ignored otherwise.
        containerService.Create(name.Trim(), type, slotsPerPage: 9);
        Load();
    }

    public bool TryRename(int id, string newName, out string? error)
    {
        var trimmed = (newName ?? "").Trim();
        if (trimmed.Length == 0)
        {
            error = "Name can't be empty.";
            return false;
        }
        if (containerService.NameExists(trimmed, excludeId: id))
        {
            error = "This name is already in use";
            return false;
        }
        containerService.Rename(id, trimmed);
        Load();
        error = null;
        return true;
    }

    public void DeleteLocation(int id, bool moveCardsToBulk)
    {
        containerService.Delete(id, moveCardsToBulk);
        Load();
    }

    internal void SaveCollapseState()
    {
        var state = Groups.ToDictionary(g => g.ContainerType.ToString(), g => g.IsExpanded);
        StorageManagerUiState.SaveExpandedState(dataPathService.DataDirectory, state);
    }

    [RelayCommand]
    public void CopyQrText(int containerId)
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
            return;
        var text = $"displaybarcode \"{BaseUrl.TrimEnd('/')}/location/{containerId}\" QR \\q 3";
        System.Windows.Clipboard.SetText(text);
    }

    [RelayCommand]
    public void ToggleDeckCheckExclusion(ContainerDisplayItem? item)
    {
        if (item is null) return;
        var newValue = !item.ExcludeFromDeckCheck;
        containerService.SetExcludeFromDeckCheck(item.Id, newValue);
        Load();
    }

    [RelayCommand]
    public void ToggleAlwaysAvailable(ContainerDisplayItem? item)
    {
        // The system Bulk location is always available and can't be toggled off.
        if (item is null || item.IsSystem) return;
        containerService.SetAlwaysAvailable(item.Id, !item.AlwaysAvailable);
        Load();
    }

    [RelayCommand]
    public void UseMyIp()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 80);
            var localIp = ((IPEndPoint)socket.LocalEndPoint!).Address.ToString();
            BaseUrl = $"http://{localIp}/";
        }
        catch
        {
            // Fallback: scan network interfaces
            var host = Dns.GetHostEntry(Dns.GetHostName());
            var ip = host.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            BaseUrl = ip is not null ? $"http://{ip}/" : "http://localhost/";
        }
    }

    [RelayCommand]
    public void Close()
    {
        SaveBaseUrl();
        CloseDialog?.Invoke();
    }

    private void SaveBaseUrl()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path)) return;
        var json = JsonNode.Parse(File.ReadAllText(path));
        if (json is null) return;
        json["WebCompanion"] ??= new JsonObject();
        json["WebCompanion"]!["BaseUrl"] = BaseUrl;
        File.WriteAllText(path, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>A collapsible group of locations of one <see cref="ContainerType"/>, with an inline
/// "add" field (name + validation) at its top.</summary>
public sealed partial class LocationGroupViewModel : ObservableObject
{
    private readonly StorageManagerViewModel _parent;

    private readonly bool _initialized;

    public LocationGroupViewModel(StorageManagerViewModel parent, ContainerType type, string typeDisplay, bool isExpanded)
    {
        _parent = parent;
        ContainerType = type;
        TypeDisplay = typeDisplay;
        IsExpanded = isExpanded;
        _initialized = true; // don't persist the constructor's initial value
    }

    public ContainerType ContainerType { get; }
    public string TypeDisplay { get; }
    public ObservableCollection<ContainerDisplayItem> Items { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    partial void OnIsExpandedChanged(bool value)
    {
        if (_initialized)
            _parent.SaveCollapseState();
    }

    [ObservableProperty]
    public partial string NewName { get; set; } = "";

    [ObservableProperty]
    public partial string NameError { get; set; } = "";

    public bool CanAdd { get; private set; }

    partial void OnNewNameChanged(string value) => RevalidateNewName();

    /// <summary>Re-runs uniqueness validation on the inline add field. Called on each keystroke and
    /// after a reload (the set of existing names may have changed).</summary>
    public void RevalidateNewName()
    {
        var trimmed = (NewName ?? "").Trim();
        if (trimmed.Length == 0)
        {
            NameError = "";
            CanAdd = false;
        }
        else if (_parent.NameInUse(trimmed))
        {
            NameError = "This name is already in use";
            CanAdd = false;
        }
        else
        {
            NameError = "";
            CanAdd = true;
        }
        OnPropertyChanged(nameof(CanAdd));
        AddCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        var trimmed = (NewName ?? "").Trim();
        if (trimmed.Length == 0 || _parent.NameInUse(trimmed))
            return;
        NewName = "";
        _parent.AddLocation(ContainerType, trimmed);
    }
}

public sealed partial class ContainerDisplayItem : ObservableObject
{
    public int Id { get; init; }

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    public ContainerType ContainerType { get; init; }
    public bool IsSystem { get; init; }
    public int CardCount { get; init; }
    public bool ExcludeFromDeckCheck { get; init; }
    public bool AlwaysAvailable { get; init; }

    /// <summary>True while the name is being edited inline (double-click to rename).</summary>
    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    /// <summary>Working copy of the name shown in the inline rename textbox; committed on Enter/blur.</summary>
    [ObservableProperty]
    public partial string EditName { get; set; } = "";

    /// <summary>The system Bulk location is always available intrinsically; its checkbox reflects
    /// that (checked) but is disabled so it can't be turned off.</summary>
    public bool IsAlwaysAvailable => IsSystem || AlwaysAvailable;

    public string TypeDisplay => ContainerType switch
    {
        ContainerType.Bulk => "Bulk",
        ContainerType.Binder => "Binder",
        ContainerType.Box => "Box",
        ContainerType.DeckBox => "Deck Box",
        ContainerType.DisplayCase => "Display Case",
        _ => ContainerType.ToString(),
    };
}
