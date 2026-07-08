using System.IO;
using System.Text.Json;
using OmniCard.Data;
using OmniCard.Models;
using OmniCard.Collection;

namespace OmniCard.Tests.Services;

public class CollectionPresetServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public CollectionPresetServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"preset-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "collection-presets.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private CollectionPresetService CreateService()
    {
        // Write a datapath.json so DataPathService resolves DataDirectory to _tempDir
        var datapathJson = System.Text.Json.JsonSerializer.Serialize(new { dataDirectory = _tempDir });
        File.WriteAllText(Path.Combine(_tempDir, "datapath.json"), datapathJson);
        return new CollectionPresetService(new DataPathService(_tempDir));
    }

    [Fact]
    public void GetSortPresets_EmptyFile_ReturnsEmptyList()
    {
        var service = CreateService();
        var presets = service.GetSortPresets(CardGame.Mtg);
        Assert.Empty(presets);
    }

    [Fact]
    public void SaveSortPreset_ThenGet_RoundTrips()
    {
        var service = CreateService();
        var preset = new SortPreset
        {
            Name = "Color Sort",
            Game = CardGame.Mtg,
            SortLevels =
            [
                new SortLevel { Field = "Color", Direction = SortDirection.Ascending, CustomOrder = ["W", "U", "B", "R", "G"] },
                new SortLevel { Field = "Name", Direction = SortDirection.Ascending }
            ]
        };

        service.SaveSortPreset(preset);
        var loaded = service.GetSortPresets(CardGame.Mtg);

        Assert.Single(loaded);
        Assert.Equal("Color Sort", loaded[0].Name);
        Assert.Equal(2, loaded[0].SortLevels.Count);
        Assert.Equal(5, loaded[0].SortLevels[0].CustomOrder!.Count);
        Assert.Null(loaded[0].SortLevels[1].CustomOrder);
    }

    [Fact]
    public void SaveSortPreset_SameName_Overwrites()
    {
        var service = CreateService();
        service.SaveSortPreset(new SortPreset
        {
            Name = "My Sort",
            Game = CardGame.Mtg,
            SortLevels = [new SortLevel { Field = "Name", Direction = SortDirection.Ascending }]
        });
        service.SaveSortPreset(new SortPreset
        {
            Name = "My Sort",
            Game = CardGame.Mtg,
            SortLevels = [new SortLevel { Field = "Color", Direction = SortDirection.Descending }]
        });

        var loaded = service.GetSortPresets(CardGame.Mtg);
        Assert.Single(loaded);
        Assert.Equal("Color", loaded[0].SortLevels[0].Field);
    }

    [Fact]
    public void DeleteSortPreset_RemovesPreset()
    {
        var service = CreateService();
        service.SaveSortPreset(new SortPreset
        {
            Name = "To Delete",
            Game = CardGame.Mtg,
            SortLevels = [new SortLevel { Field = "Name", Direction = SortDirection.Ascending }]
        });

        service.DeleteSortPreset("To Delete", CardGame.Mtg);

        Assert.Empty(service.GetSortPresets(CardGame.Mtg));
    }

    [Fact]
    public void GetSortPresets_FiltersByGame()
    {
        var service = CreateService();
        service.SaveSortPreset(new SortPreset { Name = "MTG Sort", Game = CardGame.Mtg, SortLevels = [new SortLevel { Field = "Name", Direction = SortDirection.Ascending }] });
        service.SaveSortPreset(new SortPreset { Name = "OP Sort", Game = CardGame.OnePiece, SortLevels = [new SortLevel { Field = "Name", Direction = SortDirection.Ascending }] });

        Assert.Single(service.GetSortPresets(CardGame.Mtg));
        Assert.Single(service.GetSortPresets(CardGame.OnePiece));
    }

    [Fact]
    public void SaveFilterPreset_ThenGet_RoundTrips()
    {
        var service = CreateService();
        var preset = new FilterPreset
        {
            Name = "Blue Only",
            Game = CardGame.Mtg,
            Query = "c:u"
        };

        service.SaveFilterPreset(preset);
        var loaded = service.GetFilterPresets(CardGame.Mtg);

        Assert.Single(loaded);
        Assert.Equal("Blue Only", loaded[0].Name);
        Assert.Equal("c:u", loaded[0].Query);
    }

    [Fact]
    public void SetActiveSortPreset_PersistsAcrossInstances()
    {
        var service1 = CreateService();
        service1.SaveSortPreset(new SortPreset { Name = "My Sort", Game = CardGame.Mtg, SortLevels = [new SortLevel { Field = "Name", Direction = SortDirection.Ascending }] });
        service1.SetActiveSortPreset(CardGame.Mtg, "My Sort");

        var service2 = CreateService();
        var active = service2.GetActiveSortPreset(CardGame.Mtg);

        Assert.NotNull(active);
        Assert.Equal("My Sort", active!.Name);
    }

    [Fact]
    public void SetActiveSortPreset_Null_ClearsActive()
    {
        var service = CreateService();
        service.SaveSortPreset(new SortPreset { Name = "My Sort", Game = CardGame.Mtg, SortLevels = [new SortLevel { Field = "Name", Direction = SortDirection.Ascending }] });
        service.SetActiveSortPreset(CardGame.Mtg, "My Sort");
        service.SetActiveSortPreset(CardGame.Mtg, null);

        Assert.Null(service.GetActiveSortPreset(CardGame.Mtg));
    }

    [Fact]
    public void PersistsToJsonFile()
    {
        var service = CreateService();
        service.SaveSortPreset(new SortPreset { Name = "Test", Game = CardGame.Mtg, SortLevels = [] });

        Assert.True(File.Exists(_filePath));
        var json = File.ReadAllText(_filePath);
        Assert.Contains("Test", json);
    }
}
