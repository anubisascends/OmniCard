using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using OmniCard.Models;

namespace OmniCard.Views.HashPreview;

public partial class HashStageImageItem : ObservableObject
{
    public string StageName { get; init; } = "";
    public BitmapImage Image { get; init; } = null!;
}

public sealed partial class HashPreviewViewModel : ViewModel
{
    public ObservableCollection<HashStageImageItem> Stages { get; } = [];

    [ObservableProperty]
    public partial string HashText { get; set; } = "";

    public void AddStage(HashStageResult result)
    {
        using var ms = new MemoryStream(result.ImageData);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = ms;
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();

        Stages.Add(new HashStageImageItem
        {
            StageName = result.StageName,
            Image = bmp,
        });
    }

    public void Clear()
    {
        Stages.Clear();
        HashText = "";
    }
}
