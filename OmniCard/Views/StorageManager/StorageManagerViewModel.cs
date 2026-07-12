using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.StorageManager;

public sealed partial class StorageManagerViewModel(
    IStorageContainerService containerService,
    IOptionsMonitor<WebCompanionSettings> webCompanionSettings) : ViewModel
{
    public ObservableCollection<ContainerDisplayItem> Containers { get; } = [];
    public ListCollectionView GroupedContainers { get; private set; } = null!;

    [ObservableProperty]
    public partial ContainerDisplayItem? SelectedContainer { get; set; }

    [ObservableProperty]
    public partial string NewContainerName { get; set; } = "";

    [ObservableProperty]
    public partial ContainerType NewContainerType { get; set; } = ContainerType.Binder;

    [ObservableProperty]
    public partial bool IsAdding { get; set; }

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial string EditName { get; set; } = "";

    [ObservableProperty]
    public partial string BaseUrl { get; set; } = "";

    public Action? CloseDialog { get; set; }

    partial void OnSelectedContainerChanged(ContainerDisplayItem? value)
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanDelete));
    }

    public bool CanEdit => SelectedContainer is { IsSystem: false };
    public bool CanDelete => SelectedContainer is not null && !SelectedContainer.IsSystem;

    public void Load()
    {
        BaseUrl = webCompanionSettings.CurrentValue.BaseUrl;
        var previousSelectedId = SelectedContainer?.Id;
        Containers.Clear();
        foreach (var c in containerService.GetAll().OrderBy(c => c.ContainerType).ThenBy(c => c.Name))
        {
            Containers.Add(new ContainerDisplayItem
            {
                Id = c.Id,
                Name = c.Name,
                ContainerType = c.ContainerType,
                IsSystem = c.IsSystem,
                CardCount = containerService.GetCardCount(c.Id),
                ExcludeFromDeckCheck = c.ExcludeFromDeckCheck,
            });
        }

        GroupedContainers = new ListCollectionView(Containers);
        GroupedContainers.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ContainerDisplayItem.TypeDisplay)));
        OnPropertyChanged(nameof(GroupedContainers));

        // Restore selection if the item still exists
        if (previousSelectedId is not null)
            SelectedContainer = Containers.FirstOrDefault(c => c.Id == previousSelectedId);
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
    public void ShowAdd()
    {
        IsAdding = true;
        IsEditing = false;
        NewContainerName = "";
        NewContainerType = ContainerType.Binder;
    }

    [RelayCommand]
    public void ConfirmAdd()
    {
        if (string.IsNullOrWhiteSpace(NewContainerName))
            return;

        containerService.Create(NewContainerName.Trim(), NewContainerType);
        IsAdding = false;
        Load();
    }

    [RelayCommand]
    public void CancelAdd()
    {
        IsAdding = false;
    }

    [RelayCommand]
    public void ShowEdit()
    {
        if (SelectedContainer is null || SelectedContainer.IsSystem)
            return;
        IsEditing = true;
        IsAdding = false;
        EditName = SelectedContainer.Name;
    }

    [RelayCommand]
    public void ConfirmEdit()
    {
        if (SelectedContainer is null || string.IsNullOrWhiteSpace(EditName))
            return;

        containerService.Rename(SelectedContainer.Id, EditName.Trim());
        IsEditing = false;
        Load();
    }

    [RelayCommand]
    public void CancelEdit()
    {
        IsEditing = false;
    }

    [RelayCommand]
    public void DeleteSelected()
    {
        if (SelectedContainer is null || SelectedContainer.IsSystem)
            return;

        var cardCount = containerService.GetCardCount(SelectedContainer.Id);
        if (cardCount > 0)
        {
            // Ask user: move to Bulk or delete all items?
            var result = System.Windows.MessageBox.Show(
                $"\"{SelectedContainer.Name}\" contains {cardCount} card(s).\n\n" +
                "Yes = Move cards to Bulk and delete location\n" +
                "No = Delete all cards and location\n" +
                "Cancel = Keep location",
                "Delete Location",
                System.Windows.MessageBoxButton.YesNoCancel,
                System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Cancel)
                return;

            if (result == System.Windows.MessageBoxResult.No)
            {
                // Double-confirm before deleting all cards
                var confirm = System.Windows.MessageBox.Show(
                    $"Are you sure you want to permanently delete {cardCount} card(s) from \"{SelectedContainer.Name}\"?\n\nThis cannot be undone.",
                    "Confirm Delete All Cards",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Exclamation);
                if (confirm != System.Windows.MessageBoxResult.Yes)
                    return;

                containerService.Delete(SelectedContainer.Id, moveCardsToBulk: false);
            }
            else
            {
                containerService.Delete(SelectedContainer.Id, moveCardsToBulk: true);
            }
        }
        else
        {
            containerService.Delete(SelectedContainer.Id);
        }

        SelectedContainer = null;
        Load();
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

public class ContainerDisplayItem
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public ContainerType ContainerType { get; init; }
    public bool IsSystem { get; init; }
    public int CardCount { get; init; }
    public bool ExcludeFromDeckCheck { get; init; }

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
