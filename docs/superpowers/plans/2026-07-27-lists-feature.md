# Lists Feature Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a persisted, per-game "Lists" feature where users build named card lists (manual add / URL import / paste), with cheapest-printing resolution and on-demand decklist reports.

**Architecture:** New `CardList`/`CardListItem` EF entities persisted via `OmniCardDbContext` + `UnifiedMigrationService` DDL (mirroring the `Order`/`OrderLine` precedent). A new `IListService`/`ListService` (in `OmniCard.Collection`, using `IDbContextFactory`) handles CRUD, cheapest-printing resolution, and projection to `DecklistEntry`. A new `ListsViewModel` + `ListsView` sidebar tab drives the UI, reusing the existing `SearchCards`, `ParseDecklistText`, `CheckAgainstCollection`, and `IDecklistPdfExporter` machinery. `DecklistService.CheckAgainstCollection` is generalized from hardcoded MTG to a game parameter.

**Tech Stack:** .NET 10, C#, WPF, MaterialDesign in XAML, CommunityToolkit.Mvvm (source-generated `[ObservableProperty]`/`[RelayCommand]`), EF Core + SQLite, xUnit.

## Global Constraints

- Target framework is .NET 10; C# with primary-constructor DI as used throughout.
- **No EF migrations.** Schema is created by `EnsureCreated()` (fresh DB) AND hand-written `CREATE TABLE IF NOT EXISTS` DDL in `UnifiedMigrationService.EnsureUnifiedSchema` (existing DBs). Every new table needs BOTH.
- **SQLite stores `decimal` as `TEXT`.** Declare money columns `TEXT`; never `SUM` decimals server-side — aggregate client-side after `AsEnumerable()`.
- Services use `IDbContextFactory<OmniCardDbContext>`, create-per-operation, `AsNoTracking()` for reads.
- Enum columns use `.HasConversion<string>()` in `OnModelCreating` and `TEXT` in DDL.
- **WPF dark theme:** bare `TextBlock`s render near-black; set explicit `Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"` on every text-bearing element.
- **Append the new tab at the end** of the `TabControl` (index 4) — hardcoded tab-index literals elsewhere in `RootViewModel` break if tabs are inserted in the middle.
- Service tests use xUnit + in-memory SQLite (`Data Source=:memory:`, keep the connection open, `EnsureCreated()`), following `OmniCard.Tests/Services/DecklistMatchingTests.cs`.
- Work happens on branch `feat/lists-feature` (already created).

---

## File Structure

**Create:**
- `OmniCard.Shared/Models/CardList.cs` — `CardList`, `CardListItem`, `ListItemSource` enum, `AddCardsResult` record.
- `OmniCard.Shared/Interfaces/IListService.cs` — the list service contract.
- `OmniCard.Collection/ListService.cs` — the implementation.
- `OmniCard.Tests/Services/ListServiceTests.cs` — service unit tests.
- `OmniCard/Views/Lists/ListsView.xaml` + `ListsView.xaml.cs` — the tab UserControl.
- `OmniCard/Views/Lists/ListsViewModel.cs` — the tab view model.
- `OmniCard.Tests/Services/ListsViewModelTests.cs` — VM command-logic tests.

**Modify:**
- `OmniCard.Data/OmniCardDbContext.cs` — add DbSets + config blocks.
- `OmniCard.Data/UnifiedMigrationService.cs` — add CREATE TABLE DDL.
- `OmniCard.Shared/Interfaces/IDecklistService.cs` — add `CardGame` param to `CheckAgainstCollection`.
- `OmniCard.Collection/DecklistService.cs` — thread game through `CheckAgainstCollection`.
- `OmniCard/Views/DecklistCheck/DecklistCheckViewModel.cs` — pass `CardGame.Mtg` at existing call sites.
- `OmniCard.Tests/Services/DecklistMatchingTests.cs` — update call sites for new signature.
- `OmniCard/App.xaml.cs` — DI registration.
- `OmniCard/Views/Root/RootViewModel.cs` — `Lists` property + `SetGame` hook.
- `OmniCard/Views/Root/RootView.xaml` — new `TabItem`.
- `OmniCard/Views/Root/RootView.xaml.cs` — lazy-load hook (optional).

---

## Task 1: Data model + persistence

**Files:**
- Create: `OmniCard.Shared/Models/CardList.cs`
- Modify: `OmniCard.Data/OmniCardDbContext.cs` (DbSets after line 20; config block after the `OrderLine` block ~line 169)
- Modify: `OmniCard.Data/UnifiedMigrationService.cs` (after the `OrderLines` DDL, ~line 242)
- Test: `OmniCard.Tests/Services/ListServiceTests.cs` (persistence round-trip test lives here to keep list tests together)

**Interfaces:**
- Produces: `CardList { int Id; string Name; CardGame Game; DateTime CreatedUtc; string? Notes }`, `CardListItem { int Id; int CardListId; int Quantity; string GameCardId; string CardName; string? SetCode; string? CollectorNumber; bool IsFoil; decimal? AddedMarketPrice; bool IsUnpriced; ListItemSource Source }`, `enum ListItemSource { Manual, Url, Paste }`, `record AddCardsResult(int AddedCount, IReadOnlyList<string> UnresolvedNames)`.

- [ ] **Step 1: Create the model file**

Create `OmniCard.Shared/Models/CardList.cs`:

```csharp
namespace OmniCard.Models;

public enum ListItemSource { Manual, Url, Paste }

public class CardList
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public CardGame Game { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
}

public class CardListItem
{
    public int Id { get; set; }
    public int CardListId { get; set; }
    public int Quantity { get; set; } = 1;

    // Frozen printing (resolved at add/refresh time)
    public string GameCardId { get; set; } = "";
    public string CardName { get; set; } = "";
    public string? SetCode { get; set; }
    public string? CollectorNumber { get; set; }
    public bool IsFoil { get; set; }

    /// <summary>Market price captured when the printing was resolved; null if unpriced.</summary>
    public decimal? AddedMarketPrice { get; set; }
    /// <summary>True when no printing had a price and a fallback printing was chosen.</summary>
    public bool IsUnpriced { get; set; }

    public ListItemSource Source { get; set; }
}

public record AddCardsResult(int AddedCount, IReadOnlyList<string> UnresolvedNames);
```

- [ ] **Step 2: Add DbSets**

In `OmniCard.Data/OmniCardDbContext.cs`, after line 20 (`public DbSet<MigrationState> ...`):

```csharp
    public DbSet<CardList> CardLists => Set<CardList>();
    public DbSet<CardListItem> CardListItems => Set<CardListItem>();
```

- [ ] **Step 3: Add model configuration**

In `OmniCard.Data/OmniCardDbContext.cs`, inside `OnModelCreating`, after the `OrderLine` block (before the `MigrationState` block ~line 171):

```csharp
        modelBuilder.Entity<CardList>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).ValueGeneratedOnAdd();
            e.Property(l => l.Game).HasConversion<string>();
        });

        modelBuilder.Entity<CardListItem>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).ValueGeneratedOnAdd();
            e.Property(i => i.Source).HasConversion<string>();
            e.HasIndex(i => i.CardListId);
        });
```

- [ ] **Step 4: Add migration DDL**

In `OmniCard.Data/UnifiedMigrationService.cs`, after the `OrderLines` index creation (~line 242, before the `MismatchLogs` block):

```csharp
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS CardLists (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL DEFAULT '',
                Game TEXT NOT NULL DEFAULT 'Mtg',
                CreatedUtc TEXT NOT NULL,
                Notes TEXT
            )
            """;
        cmd.ExecuteNonQuery();

        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS CardListItems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CardListId INTEGER NOT NULL,
                Quantity INTEGER NOT NULL DEFAULT 1,
                GameCardId TEXT NOT NULL DEFAULT '',
                CardName TEXT NOT NULL DEFAULT '',
                SetCode TEXT,
                CollectorNumber TEXT,
                IsFoil INTEGER NOT NULL DEFAULT 0,
                AddedMarketPrice TEXT,
                IsUnpriced INTEGER NOT NULL DEFAULT 0,
                Source TEXT NOT NULL DEFAULT 'Manual'
            )
            """;
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_CardListItems_CardListId ON CardListItems(CardListId)";
        cmd.ExecuteNonQuery();
```

- [ ] **Step 5: Write the failing persistence test**

Create `OmniCard.Tests/Services/ListServiceTests.cs` with the in-memory harness and a round-trip test. (Later tasks add more `[Fact]`s to this same file and the fakes.)

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class ListServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<OmniCardDbContext> _dbFactory;

    public ListServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OmniCardDbContext>()
            .UseSqlite(_connection).Options;
        _dbFactory = new TestOmniDbFactory(options);
        using var ctx = _dbFactory.CreateDbContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private sealed class TestOmniDbFactory(DbContextOptions<OmniCardDbContext> options)
        : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }

    [Fact]
    public void CardList_And_Items_RoundTrip()
    {
        using (var ctx = _dbFactory.CreateDbContext())
        {
            var list = new CardList { Name = "Budget Deck", Game = CardGame.Mtg };
            ctx.CardLists.Add(list);
            ctx.SaveChanges();
            ctx.CardListItems.Add(new CardListItem
            {
                CardListId = list.Id, Quantity = 2, GameCardId = "abc",
                CardName = "Sol Ring", SetCode = "C21", AddedMarketPrice = 1.23m,
                Source = ListItemSource.Paste,
            });
            ctx.SaveChanges();
        }

        using (var ctx = _dbFactory.CreateDbContext())
        {
            var list = Assert.Single(ctx.CardLists.AsNoTracking().ToList());
            Assert.Equal("Budget Deck", list.Name);
            Assert.Equal(CardGame.Mtg, list.Game);
            var item = Assert.Single(ctx.CardListItems.AsNoTracking().ToList());
            Assert.Equal(2, item.Quantity);
            Assert.Equal(1.23m, item.AddedMarketPrice);
            Assert.Equal(ListItemSource.Paste, item.Source);
        }
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~ListServiceTests.CardList_And_Items_RoundTrip"`
Expected: PASS (schema built by `EnsureCreated`, enums round-trip as strings, decimal round-trips via TEXT).

- [ ] **Step 7: Commit**

```bash
git add OmniCard.Shared/Models/CardList.cs OmniCard.Data/OmniCardDbContext.cs OmniCard.Data/UnifiedMigrationService.cs OmniCard.Tests/Services/ListServiceTests.cs
git commit -m "feat(lists): add CardList/CardListItem model + persistence"
```

---

## Task 2: IListService + ListService CRUD

**Files:**
- Create: `OmniCard.Shared/Interfaces/IListService.cs`
- Create: `OmniCard.Collection/ListService.cs`
- Test: `OmniCard.Tests/Services/ListServiceTests.cs` (add facts + fakes)

**Interfaces:**
- Consumes: `CardList`, `CardListItem`, `ListItemSource`, `AddCardsResult` (Task 1); `CardMatch`, `DecklistEntry`, `ICardService.GetGameService`, `ICardGameService.GetCurrentPrice/GetCurrentPrices/GetPrintings`.
- Produces: `IListService` with `GetLists(CardGame)`, `CreateList(string,CardGame)`, `RenameList(int,string)`, `DeleteList(int)`, `GetItems(int)`, `AddPrinting(int,CardMatch,bool,int,ListItemSource)`, `RemoveItem(int)`, `SetQuantity(int,int)`, `AddCardsByName(int,IEnumerable<DecklistEntry>)`, `RefreshPrices(int)`, `ToDecklistEntries(int)`. (Task 3 implements the last three; this task stubs them to throw `NotImplementedException` so the interface compiles.)

- [ ] **Step 1: Create the interface**

Create `OmniCard.Shared/Interfaces/IListService.cs`:

```csharp
using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IListService
{
    IReadOnlyList<CardList> GetLists(CardGame game);
    CardList CreateList(string name, CardGame game);
    void RenameList(int listId, string name);
    void DeleteList(int listId);

    IReadOnlyList<CardListItem> GetItems(int listId);
    CardListItem AddPrinting(int listId, CardMatch printing, bool isFoil, int quantity, ListItemSource source);
    void RemoveItem(int itemId);
    void SetQuantity(int itemId, int quantity);

    // Implemented in Task 3
    AddCardsResult AddCardsByName(int listId, IEnumerable<DecklistEntry> entries);
    void RefreshPrices(int listId);
    List<DecklistEntry> ToDecklistEntries(int listId);
}
```

- [ ] **Step 2: Write failing CRUD tests**

Add to `OmniCard.Tests/Services/ListServiceTests.cs`. First add a factory helper and a configurable fake card service near the bottom of the class (copy the private `StubCardService` and `StubGameService` classes from `OmniCard.Tests/Services/DecklistMatchingTests.cs` into this file, renamed `FakeCardService`/`FakeGameService`, then add these mutable members to `FakeGameService` and override the three methods):

```csharp
    // Add to FakeGameService (mirrors StubGameService from DecklistMatchingTests):
    public List<CardMatch> Printings { get; } = [];
    public Dictionary<string, decimal> Prices { get; } = new();
    // Replace the three stubbed bodies with:
    //   GetPrintings   -> Printings.Where(p => p.Name == cardName).ToList();
    //   GetCurrentPrices(ids, foil) -> ids.Where(Prices.ContainsKey).ToDictionary(id => id, id => Prices[id]);
    //   GetCurrentPrice(id, foil)   -> Prices.TryGetValue(id, out var v) ? v : null;

    // Add to FakeCardService (mirrors StubCardService), holding one shared game service:
    public FakeGameService Game { get; } = new();
    // Replace GetGameService(game) -> Game;
```

Then the CRUD facts:

```csharp
    private ListService CreateService(FakeCardService? cards = null)
        => new(_dbFactory, cards ?? new FakeCardService());

    [Fact]
    public void CreateList_Then_GetLists_FiltersByGame()
    {
        var svc = CreateService();
        svc.CreateList("MTG list", CardGame.Mtg);
        svc.CreateList("PKM list", CardGame.Pokemon);

        var mtg = svc.GetLists(CardGame.Mtg);
        Assert.Single(mtg);
        Assert.Equal("MTG list", mtg[0].Name);
    }

    [Fact]
    public void RenameList_UpdatesName()
    {
        var svc = CreateService();
        var list = svc.CreateList("old", CardGame.Mtg);
        svc.RenameList(list.Id, "new");
        Assert.Equal("new", svc.GetLists(CardGame.Mtg).Single().Name);
    }

    [Fact]
    public void DeleteList_RemovesListAndItems()
    {
        var svc = CreateService();
        var list = svc.CreateList("L", CardGame.Mtg);
        svc.AddPrinting(list.Id, new CardMatch { Name = "Sol Ring", GameSpecificId = "x" },
            isFoil: false, quantity: 1, ListItemSource.Manual);

        svc.DeleteList(list.Id);

        Assert.Empty(svc.GetLists(CardGame.Mtg));
        using var ctx = _dbFactory.CreateDbContext();
        Assert.Empty(ctx.CardListItems.AsNoTracking().ToList());
    }

    [Fact]
    public void AddPrinting_CapturesPrice_AndMergesDuplicate()
    {
        var cards = new FakeCardService();
        cards.Game.Prices["x"] = 2.50m;
        var svc = CreateService(cards);
        var list = svc.CreateList("L", CardGame.Mtg);
        var match = new CardMatch { Name = "Sol Ring", GameSpecificId = "x", SetCode = "C21", CollectorNumber = "1" };

        svc.AddPrinting(list.Id, match, isFoil: false, quantity: 1, ListItemSource.Manual);
        svc.AddPrinting(list.Id, match, isFoil: false, quantity: 2, ListItemSource.Manual);

        var item = Assert.Single(svc.GetItems(list.Id));
        Assert.Equal(3, item.Quantity);          // merged
        Assert.Equal(2.50m, item.AddedMarketPrice);
        Assert.Equal("Sol Ring", item.CardName);
    }

    [Fact]
    public void SetQuantity_Zero_RemovesItem()
    {
        var svc = CreateService();
        var list = svc.CreateList("L", CardGame.Mtg);
        var item = svc.AddPrinting(list.Id, new CardMatch { Name = "A", GameSpecificId = "x" },
            false, 1, ListItemSource.Manual);
        svc.SetQuantity(item.Id, 0);
        Assert.Empty(svc.GetItems(list.Id));
    }
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~ListServiceTests"`
Expected: FAIL — `ListService` does not exist / methods not implemented.

- [ ] **Step 4: Implement ListService (CRUD portion)**

Create `OmniCard.Collection/ListService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public class ListService(
    IDbContextFactory<OmniCardDbContext> dbContextFactory,
    ICardService cardService) : IListService
{
    public IReadOnlyList<CardList> GetLists(CardGame game)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.CardLists.AsNoTracking()
            .Where(l => l.Game == game)
            .OrderBy(l => l.Name)
            .ToList();
    }

    public CardList CreateList(string name, CardGame game)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var list = new CardList { Name = name, Game = game, CreatedUtc = DateTime.UtcNow };
        ctx.CardLists.Add(list);
        ctx.SaveChanges();
        return list;
    }

    public void RenameList(int listId, string name)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var list = ctx.CardLists.FirstOrDefault(l => l.Id == listId);
        if (list is null) return;
        list.Name = name;
        ctx.SaveChanges();
    }

    public void DeleteList(int listId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var list = ctx.CardLists.FirstOrDefault(l => l.Id == listId);
        if (list is null) return;
        var items = ctx.CardListItems.Where(i => i.CardListId == listId).ToList();
        ctx.CardListItems.RemoveRange(items);
        ctx.CardLists.Remove(list);
        ctx.SaveChanges();
    }

    public IReadOnlyList<CardListItem> GetItems(int listId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.CardListItems.AsNoTracking()
            .Where(i => i.CardListId == listId)
            .OrderBy(i => i.CardName)
            .ToList();
    }

    public CardListItem AddPrinting(int listId, CardMatch printing, bool isFoil, int quantity, ListItemSource source)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var list = ctx.CardLists.AsNoTracking().FirstOrDefault(l => l.Id == listId)
                   ?? throw new InvalidOperationException($"List {listId} not found.");

        var existing = ctx.CardListItems.FirstOrDefault(i =>
            i.CardListId == listId && i.GameCardId == printing.GameSpecificId && i.IsFoil == isFoil);
        if (existing is not null)
        {
            existing.Quantity += quantity;
            ctx.SaveChanges();
            return existing;
        }

        var price = cardService.GetGameService(list.Game).GetCurrentPrice(printing.GameSpecificId, isFoil);
        var item = new CardListItem
        {
            CardListId = listId,
            Quantity = quantity,
            GameCardId = printing.GameSpecificId,
            CardName = printing.Name,
            SetCode = string.IsNullOrEmpty(printing.SetCode) ? null : printing.SetCode,
            CollectorNumber = string.IsNullOrEmpty(printing.CollectorNumber) ? null : printing.CollectorNumber,
            IsFoil = isFoil,
            AddedMarketPrice = price,
            IsUnpriced = price is null,
            Source = source,
        };
        ctx.CardListItems.Add(item);
        ctx.SaveChanges();
        return item;
    }

    public void RemoveItem(int itemId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var item = ctx.CardListItems.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return;
        ctx.CardListItems.Remove(item);
        ctx.SaveChanges();
    }

    public void SetQuantity(int itemId, int quantity)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var item = ctx.CardListItems.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return;
        if (quantity <= 0) { ctx.CardListItems.Remove(item); }
        else { item.Quantity = quantity; }
        ctx.SaveChanges();
    }

    // ---- Task 3 implements these ----
    public AddCardsResult AddCardsByName(int listId, IEnumerable<DecklistEntry> entries)
        => throw new NotImplementedException();
    public void RefreshPrices(int listId) => throw new NotImplementedException();
    public List<DecklistEntry> ToDecklistEntries(int listId) => throw new NotImplementedException();
}
```

- [ ] **Step 5: Run tests to verify CRUD facts pass**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~ListServiceTests"`
Expected: PASS for the CRUD facts (the round-trip fact from Task 1 also still passes). The three not-yet-implemented methods have no tests yet.

- [ ] **Step 6: Commit**

```bash
git add OmniCard.Shared/Interfaces/IListService.cs OmniCard.Collection/ListService.cs OmniCard.Tests/Services/ListServiceTests.cs
git commit -m "feat(lists): IListService + ListService CRUD"
```

---

## Task 3: Cheapest resolver, refresh, and DecklistEntry projection

**Files:**
- Modify: `OmniCard.Collection/ListService.cs` (replace the three `NotImplementedException` bodies)
- Test: `OmniCard.Tests/Services/ListServiceTests.cs` (add facts)

**Interfaces:**
- Consumes: `ICardGameService.GetPrintings(string)`, `.GetCurrentPrices(IEnumerable<string>, bool)` (Task 2 fakes expose configurable `Printings`/`Prices`).
- Produces: working `AddCardsByName`, `RefreshPrices`, `ToDecklistEntries`.

- [ ] **Step 1: Write failing resolver tests**

Add to `ListServiceTests.cs`:

```csharp
    private static CardMatch Printing(string name, string id, string set, string cn = "1")
        => new() { Name = name, GameSpecificId = id, SetCode = set, CollectorNumber = cn };

    [Fact]
    public void AddCardsByName_PicksCheapestNonFoilPrinting()
    {
        var cards = new FakeCardService();
        cards.Game.Printings.Add(Printing("Sol Ring", "a", "C16"));
        cards.Game.Printings.Add(Printing("Sol Ring", "b", "C21"));
        cards.Game.Prices["a"] = 5.00m;
        cards.Game.Prices["b"] = 1.50m;
        var svc = CreateService(cards);
        var list = svc.CreateList("L", CardGame.Mtg);

        var result = svc.AddCardsByName(list.Id, new[] { new DecklistEntry(1, "Sol Ring", null, null) });

        Assert.Equal(1, result.AddedCount);
        Assert.Empty(result.UnresolvedNames);
        var item = Assert.Single(svc.GetItems(list.Id));
        Assert.Equal("b", item.GameCardId);        // cheapest
        Assert.Equal(1.50m, item.AddedMarketPrice);
        Assert.False(item.IsUnpriced);
    }

    [Fact]
    public void AddCardsByName_NoPrice_FallsBackToFirst_AndFlagsUnpriced()
    {
        var cards = new FakeCardService();
        cards.Game.Printings.Add(Printing("Rare Card", "a", "SET"));
        var svc = CreateService(cards);
        var list = svc.CreateList("L", CardGame.Mtg);

        svc.AddCardsByName(list.Id, new[] { new DecklistEntry(1, "Rare Card", null, null) });

        var item = Assert.Single(svc.GetItems(list.Id));
        Assert.Equal("a", item.GameCardId);
        Assert.Null(item.AddedMarketPrice);
        Assert.True(item.IsUnpriced);
    }

    [Fact]
    public void AddCardsByName_UnknownCard_ReportedUnresolved()
    {
        var svc = CreateService(new FakeCardService());
        var list = svc.CreateList("L", CardGame.Mtg);

        var result = svc.AddCardsByName(list.Id, new[] { new DecklistEntry(1, "Ghost", null, null) });

        Assert.Equal(0, result.AddedCount);
        Assert.Equal(new[] { "Ghost" }, result.UnresolvedNames);
        Assert.Empty(svc.GetItems(list.Id));
    }

    [Fact]
    public void AddCardsByName_MergesQuantityForSameResolvedPrinting()
    {
        var cards = new FakeCardService();
        cards.Game.Printings.Add(Printing("Island", "isl", "SET"));
        cards.Game.Prices["isl"] = 0.10m;
        var svc = CreateService(cards);
        var list = svc.CreateList("L", CardGame.Mtg);

        svc.AddCardsByName(list.Id, new[]
        {
            new DecklistEntry(3, "Island", null, null),
            new DecklistEntry(2, "Island", null, null),
        });

        var item = Assert.Single(svc.GetItems(list.Id));
        Assert.Equal(5, item.Quantity);
    }

    [Fact]
    public void ToDecklistEntries_ProjectsQuantityAndSet()
    {
        var cards = new FakeCardService();
        cards.Game.Prices["x"] = 1m;
        var svc = CreateService(cards);
        var list = svc.CreateList("L", CardGame.Mtg);
        svc.AddPrinting(list.Id, Printing("Sol Ring", "x", "C21", "1"), false, 2, ListItemSource.Manual);

        var entries = svc.ToDecklistEntries(list.Id);

        var e = Assert.Single(entries);
        Assert.Equal(2, e.Quantity);
        Assert.Equal("Sol Ring", e.CardName);
        Assert.Equal("C21", e.SetCode);
    }

    [Fact]
    public void RefreshPrices_Manual_UpdatesPriceKeepsPrinting()
    {
        var cards = new FakeCardService();
        cards.Game.Prices["x"] = 1.00m;
        var svc = CreateService(cards);
        var list = svc.CreateList("L", CardGame.Mtg);
        svc.AddPrinting(list.Id, Printing("Sol Ring", "x", "C21"), false, 1, ListItemSource.Manual);

        cards.Game.Prices["x"] = 3.00m;
        svc.RefreshPrices(list.Id);

        var item = Assert.Single(svc.GetItems(list.Id));
        Assert.Equal("x", item.GameCardId);           // printing unchanged
        Assert.Equal(3.00m, item.AddedMarketPrice);
    }

    [Fact]
    public void RefreshPrices_Paste_ReResolvesCheapest()
    {
        var cards = new FakeCardService();
        cards.Game.Printings.Add(Printing("Sol Ring", "a", "C16"));
        cards.Game.Printings.Add(Printing("Sol Ring", "b", "C21"));
        cards.Game.Prices["a"] = 5m; cards.Game.Prices["b"] = 2m;
        var svc = CreateService(cards);
        var list = svc.CreateList("L", CardGame.Mtg);
        svc.AddCardsByName(list.Id, new[] { new DecklistEntry(1, "Sol Ring", null, null) }); // picks "b" @2

        cards.Game.Prices["a"] = 1m; // now "a" is cheapest
        svc.RefreshPrices(list.Id);

        var item = Assert.Single(svc.GetItems(list.Id));
        Assert.Equal("a", item.GameCardId);
        Assert.Equal(1m, item.AddedMarketPrice);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~ListServiceTests"`
Expected: FAIL — `NotImplementedException` from the three methods.

- [ ] **Step 3: Implement the three methods**

In `OmniCard.Collection/ListService.cs`, replace the three placeholder bodies. Add a private helper for cheapest resolution:

```csharp
    public AddCardsResult AddCardsByName(int listId, IEnumerable<DecklistEntry> entries)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var list = ctx.CardLists.AsNoTracking().FirstOrDefault(l => l.Id == listId)
                   ?? throw new InvalidOperationException($"List {listId} not found.");
        var gs = cardService.GetGameService(list.Game);

        var unresolved = new List<string>();
        var added = 0;

        foreach (var entry in entries)
        {
            var resolved = ResolveCheapest(gs, entry.CardName);
            if (resolved is null) { unresolved.Add(entry.CardName); continue; }
            var (printing, price, unpriced) = resolved.Value;

            var existing = ctx.CardListItems.FirstOrDefault(i =>
                i.CardListId == listId && i.GameCardId == printing.GameSpecificId && !i.IsFoil);
            if (existing is not null)
            {
                existing.Quantity += entry.Quantity;
            }
            else
            {
                ctx.CardListItems.Add(new CardListItem
                {
                    CardListId = listId,
                    Quantity = entry.Quantity,
                    GameCardId = printing.GameSpecificId,
                    CardName = printing.Name,
                    SetCode = string.IsNullOrEmpty(printing.SetCode) ? null : printing.SetCode,
                    CollectorNumber = string.IsNullOrEmpty(printing.CollectorNumber) ? null : printing.CollectorNumber,
                    IsFoil = false,
                    AddedMarketPrice = price,
                    IsUnpriced = unpriced,
                    Source = ListItemSource.Paste,
                });
            }
            added++;
        }

        ctx.SaveChanges();
        return new AddCardsResult(added, unresolved);
    }

    public void RefreshPrices(int listId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var list = ctx.CardLists.AsNoTracking().FirstOrDefault(l => l.Id == listId);
        if (list is null) return;
        var gs = cardService.GetGameService(list.Game);

        foreach (var item in ctx.CardListItems.Where(i => i.CardListId == listId).ToList())
        {
            if (item.Source == ListItemSource.Manual)
            {
                item.AddedMarketPrice = gs.GetCurrentPrice(item.GameCardId, item.IsFoil);
                item.IsUnpriced = item.AddedMarketPrice is null;
            }
            else
            {
                var resolved = ResolveCheapest(gs, item.CardName);
                if (resolved is null) continue; // leave as-is if no longer resolvable
                var (printing, price, unpriced) = resolved.Value;
                item.GameCardId = printing.GameSpecificId;
                item.SetCode = string.IsNullOrEmpty(printing.SetCode) ? null : printing.SetCode;
                item.CollectorNumber = string.IsNullOrEmpty(printing.CollectorNumber) ? null : printing.CollectorNumber;
                item.AddedMarketPrice = price;
                item.IsUnpriced = unpriced;
            }
        }
        ctx.SaveChanges();
    }

    public List<DecklistEntry> ToDecklistEntries(int listId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.CardListItems.AsNoTracking()
            .Where(i => i.CardListId == listId)
            .AsEnumerable()
            .Select(i => new DecklistEntry(i.Quantity, i.CardName, i.SetCode, i.CollectorNumber))
            .ToList();
    }

    /// <summary>Cheapest non-foil printing of the named card. Returns null if no printing exists;
    /// on no-price, returns the first printing flagged unpriced.</summary>
    private static (CardMatch Printing, decimal? Price, bool Unpriced)? ResolveCheapest(
        ICardGameService gs, string cardName)
    {
        var printings = gs.GetPrintings(cardName);
        if (printings.Count == 0) return null;

        var prices = gs.GetCurrentPrices(printings.Select(p => p.GameSpecificId), isFoil: false);
        var priced = printings
            .Where(p => prices.ContainsKey(p.GameSpecificId))
            .OrderBy(p => prices[p.GameSpecificId])
            .ToList();

        if (priced.Count > 0)
            return (priced[0], prices[priced[0].GameSpecificId], false);
        return (printings[0], null, true);
    }
```

Add `using OmniCard.Interfaces;` is already present; `ICardGameService` is in `OmniCard.Interfaces`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~ListServiceTests"`
Expected: PASS (all `ListServiceTests` facts).

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Collection/ListService.cs OmniCard.Tests/Services/ListServiceTests.cs
git commit -m "feat(lists): cheapest-printing resolver, refresh, and DecklistEntry projection"
```

---

## Task 4: Generalize decklist reports to a game parameter

**Files:**
- Modify: `OmniCard.Shared/Interfaces/IDecklistService.cs`
- Modify: `OmniCard.Collection/DecklistService.cs` (`CheckAgainstCollection`, ~line 187)
- Modify: `OmniCard/Views/DecklistCheck/DecklistCheckViewModel.cs` (2 call sites)
- Test: `OmniCard.Tests/Services/DecklistMatchingTests.cs` (update existing call sites)

**Interfaces:**
- Produces: `DecklistCheckResult CheckAgainstCollection(string deckName, string deckSource, List<DecklistEntry> entries, CardGame game)`.

- [ ] **Step 1: Update the interface**

In `OmniCard.Shared/Interfaces/IDecklistService.cs`, change the signature:

```csharp
    DecklistCheckResult CheckAgainstCollection(string deckName, string deckSource, List<DecklistEntry> entries, CardGame game);
```

- [ ] **Step 2: Update existing test call sites (they now fail to compile → the "failing test")**

In `OmniCard.Tests/Services/DecklistMatchingTests.cs`, every `service.CheckAgainstCollection("Test", "Test", entries)` becomes `service.CheckAgainstCollection("Test", "Test", entries, CardGame.Mtg)` (5 call sites: lines ~83, 101, 116, 130, 146).

- [ ] **Step 3: Run to confirm the build fails**

Run: `dotnet build OmniCard.Collection`
Expected: FAIL — `DecklistService` does not implement the new interface signature.

- [ ] **Step 4: Implement the generalized method**

In `OmniCard.Collection/DecklistService.cs`:
- Change the method signature (line 187) to add `, CardGame game`.
- In the collection query (~line 193), add a game filter: change `where p.Category == ProductCategory.Single` to `where p.Category == ProductCategory.Single && p.Game == game`.
- Replace both `CardGame.Mtg` usages (~line 240 `cardService.GetGameService(CardGame.Mtg)`) with `game`:

```csharp
            var gameService = cardService.GetGameService(game);
```

(There is one `GetGameService` call inside the per-entry loop; update it. The `SearchCards`/`GetCurrentPrice` calls hang off that `gameService` variable and need no further change.)

- [ ] **Step 5: Update the dialog call sites**

In `OmniCard/Views/DecklistCheck/DecklistCheckViewModel.cs`:
- `Fetch()` (~line 79): `Result = decklistService.CheckAgainstCollection(deckName, source, entries, CardGame.Mtg);`
- `ParseText()` (~line 114): `Result = decklistService.CheckAgainstCollection(deckName, "Text", entries, CardGame.Mtg);`

Add `using OmniCard.Models;` if `CardGame` is not already in scope (it is — the file already uses `OmniCard.Models`).

- [ ] **Step 6: Run the full decklist + list test suites**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~DecklistMatchingTests|FullyQualifiedName~ListServiceTests"`
Expected: PASS. (Existing MTG tests still pass because seeded products are `CardGame.Mtg` and callers pass `CardGame.Mtg`.)

- [ ] **Step 7: Commit**

```bash
git add OmniCard.Shared/Interfaces/IDecklistService.cs OmniCard.Collection/DecklistService.cs OmniCard/Views/DecklistCheck/DecklistCheckViewModel.cs OmniCard.Tests/Services/DecklistMatchingTests.cs
git commit -m "feat(lists): generalize decklist report to a game parameter"
```

---

## Task 5: DI registration + RootViewModel wiring

**Files:**
- Modify: `OmniCard/App.xaml.cs` (services block, ~line 165)
- Modify: `OmniCard/Views/Root/RootViewModel.cs` (constructor injection + `Lists` property + `OnSelectedGameChanged`)

**Interfaces:**
- Consumes: `IListService` (Task 2), `ListsViewModel` (Task 6 — this task references the type; if executing strictly in order, do Task 6 before this task's build step, OR temporarily register only `IListService` here and add the `ListsViewModel` lines when Task 6 lands). Recommended order: implement Task 6 first, then this task.
- Produces: `RootViewModel.Lists` (`ListsViewModel`), reachable from XAML as `ViewModel.Lists`.

> **Note:** Task 6 (ViewModel) and Task 5 (wiring) are interdependent for compilation. Implement **Task 6 first**, then Task 5, then Task 7 (View). The plan lists them 5→6→7 for reading order; execute 6→5→7.

- [ ] **Step 1: Register the service and view model**

In `OmniCard/App.xaml.cs`, in the services block near the Decklist registration (~line 165-167), add:

```csharp
            // Lists
            services.AddSingleton<IListService, ListService>();
            services.AddSingleton<ListsViewModel>();
```

Ensure `using OmniCard.Views.Lists;` is present at the top of `App.xaml.cs` (add if missing).

- [ ] **Step 2: Inject and expose Lists on RootViewModel**

In `OmniCard/Views/Root/RootViewModel.cs`, add a constructor parameter alongside the existing nested VMs (follow how `Sales` is injected, ~line 40) and expose it (follow ~line 174). Concretely, add the parameter `Views.Lists.ListsViewModel lists` to the primary constructor parameter list, and add the property:

```csharp
    public Views.Lists.ListsViewModel Lists { get; } = lists;
```

- [ ] **Step 3: Route game changes to Lists**

In `RootViewModel.OnSelectedGameChanged` (~line 517-563), next to the existing `Collection.SetGame(value)` call (~line 563), add:

```csharp
        Lists.SetGame(value);
```

- [ ] **Step 4: Build**

Run: `dotnet build OmniCard/OmniCard.csproj`
Expected: SUCCESS (0 errors). Requires Task 6's `ListsViewModel` to exist.

- [ ] **Step 5: Commit**

```bash
git add OmniCard/App.xaml.cs OmniCard/Views/Root/RootViewModel.cs
git commit -m "feat(lists): register list service + wire Lists view model into root"
```

---

## Task 6: ListsViewModel

**Files:**
- Create: `OmniCard/Views/Lists/ListsViewModel.cs`
- Test: `OmniCard.Tests/Services/ListsViewModelTests.cs`

**Interfaces:**
- Consumes: `IListService` (all methods), `ICardService.GetGameService(...).SearchCards`, `IDecklistService.FetchDecklistAsync/ParseDecklistText/CheckAgainstCollection`, `IDecklistPdfExporter`.
- Produces: `ListsViewModel` with `SetGame(CardGame?)`, observable `Lists`/`SelectedList`/`Items`/`SearchResults`/`Result`, input properties `NewListName`/`SearchQuery`/`PasteText`/`ImportUrl`/`SelectedSearchResult`/`SelectedItem`, `bool CanImportUrl`, `Action<DecklistCheckResult>? ExportPdf`/`ExportDetailedPdf`, and commands `CreateList`/`DeleteList`/`Search`/`AddSelectedPrinting`/`ParsePaste`/`ImportUrl`/`RefreshPrices`/`RemoveSelectedItem`/`RunSummaryReport`/`RunDetailedReport`.

- [ ] **Step 1: Write the ViewModel**

Create `OmniCard/Views/Lists/ListsViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.Lists;

public sealed partial class ListsViewModel(
    IListService listService,
    ICardService cardService,
    IDecklistService decklistService,
    ILogger<ListsViewModel> logger) : ObservableObject
{
    private CardGame? _game;

    public ObservableCollection<CardList> Lists { get; } = [];
    public ObservableCollection<CardListItem> Items { get; } = [];
    public ObservableCollection<CardMatch> SearchResults { get; } = [];

    [ObservableProperty]
    public partial CardList? SelectedList { get; set; }

    [ObservableProperty]
    public partial CardMatch? SelectedSearchResult { get; set; }

    [ObservableProperty]
    public partial CardListItem? SelectedItem { get; set; }

    [ObservableProperty]
    public partial string NewListName { get; set; } = "";

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = "";

    [ObservableProperty]
    public partial string PasteText { get; set; } = "";

    [ObservableProperty]
    public partial string ImportUrl { get; set; } = "";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IgnoreBasicLands { get; set; } = true;

    [ObservableProperty]
    public partial DecklistCheckResult? Result { get; set; }

    public Action<DecklistCheckResult>? ExportPdf { get; set; }
    public Action<DecklistCheckResult>? ExportDetailedPdf { get; set; }

    /// <summary>MTG-only: Moxfield/Archidekt URL import.</summary>
    public bool CanImportUrl => SelectedList?.Game == CardGame.Mtg;

    private static readonly HashSet<string> BasicLandNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Plains", "Island", "Swamp", "Mountain", "Forest"
    };

    public void SetGame(CardGame? game)
    {
        _game = game;
        LoadLists();
    }

    private void LoadLists()
    {
        Lists.Clear();
        Items.Clear();
        Result = null;
        SelectedList = null;
        if (_game is null) return;
        foreach (var l in listService.GetLists(_game.Value))
            Lists.Add(l);
    }

    partial void OnSelectedListChanged(CardList? value)
    {
        OnPropertyChanged(nameof(CanImportUrl));
        Result = null;
        LoadItems();
    }

    private void LoadItems()
    {
        Items.Clear();
        if (SelectedList is null) return;
        foreach (var i in listService.GetItems(SelectedList.Id))
            Items.Add(i);
    }

    [RelayCommand]
    private void CreateList()
    {
        if (_game is null) { StatusMessage = "Select a game first."; return; }
        if (string.IsNullOrWhiteSpace(NewListName)) { StatusMessage = "Enter a list name."; return; }
        var list = listService.CreateList(NewListName.Trim(), _game.Value);
        NewListName = "";
        LoadLists();
        SelectedList = Lists.FirstOrDefault(l => l.Id == list.Id);
    }

    [RelayCommand]
    private void DeleteList()
    {
        if (SelectedList is null) return;
        listService.DeleteList(SelectedList.Id);
        LoadLists();
    }

    [RelayCommand]
    private void Search()
    {
        SearchResults.Clear();
        if (_game is null || string.IsNullOrWhiteSpace(SearchQuery)) return;
        foreach (var m in cardService.GetGameService(_game.Value).SearchCards(SearchQuery.Trim(), 20))
            SearchResults.Add(m);
    }

    [RelayCommand]
    private void AddSelectedPrinting()
    {
        if (SelectedList is null || SelectedSearchResult is null) return;
        listService.AddPrinting(SelectedList.Id, SelectedSearchResult, isFoil: false, quantity: 1, ListItemSource.Manual);
        LoadItems();
    }

    [RelayCommand]
    private void ParsePaste()
    {
        if (SelectedList is null) { StatusMessage = "Select or create a list first."; return; }
        if (string.IsNullOrWhiteSpace(PasteText)) { StatusMessage = "Paste a decklist."; return; }
        var (_, entries) = decklistService.ParseDecklistText(PasteText);
        var result = listService.AddCardsByName(SelectedList.Id, entries);
        PasteText = "";
        LoadItems();
        StatusMessage = result.UnresolvedNames.Count == 0
            ? $"Added {result.AddedCount} cards."
            : $"Added {result.AddedCount}. Not found: {string.Join(", ", result.UnresolvedNames)}";
    }

    [RelayCommand]
    private async Task ImportUrlAsync()
    {
        if (SelectedList is null || !CanImportUrl) return;
        if (string.IsNullOrWhiteSpace(ImportUrl)) { StatusMessage = "Enter a URL."; return; }
        IsBusy = true;
        try
        {
            var fetched = await decklistService.FetchDecklistAsync(ImportUrl.Trim());
            if (fetched is null) { StatusMessage = "Couldn't reach the site. Paste the list instead."; return; }
            var (_, entries) = fetched.Value;
            var result = listService.AddCardsByName(SelectedList.Id, entries);
            ImportUrl = "";
            LoadItems();
            StatusMessage = result.UnresolvedNames.Count == 0
                ? $"Imported {result.AddedCount} cards."
                : $"Imported {result.AddedCount}. Not found: {string.Join(", ", result.UnresolvedNames)}";
        }
        catch (Exception ex)
        {
            StatusMessage = "Couldn't import from that URL.";
            logger.LogWarning(ex, "List URL import failed for {Url}", ImportUrl);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void RefreshPrices()
    {
        if (SelectedList is null) return;
        listService.RefreshPrices(SelectedList.Id);
        LoadItems();
        StatusMessage = "Prices refreshed.";
    }

    [RelayCommand]
    private void RemoveSelectedItem()
    {
        if (SelectedItem is null) return;
        listService.RemoveItem(SelectedItem.Id);
        LoadItems();
    }

    private DecklistCheckResult? BuildResult()
    {
        if (SelectedList is null) return null;
        var entries = listService.ToDecklistEntries(SelectedList.Id);
        if (IgnoreBasicLands)
            entries = entries.Where(e => !BasicLandNames.Contains(e.CardName)).ToList();
        var result = decklistService.CheckAgainstCollection(
            SelectedList.Name, "List", entries, SelectedList.Game);
        Result = result;
        StatusMessage = $"Owned: {result.TotalOwned}/{result.TotalCards} | Missing: {result.TotalMissing} | Cost: ${result.EstimatedCost:N2}";
        return result;
    }

    [RelayCommand]
    private void RunSummaryReport()
    {
        var result = BuildResult();
        if (result is not null) ExportPdf?.Invoke(result);
    }

    [RelayCommand]
    private void RunDetailedReport()
    {
        var result = BuildResult();
        if (result is not null) ExportDetailedPdf?.Invoke(result);
    }
}
```

- [ ] **Step 2: Write VM command-logic tests**

Create `OmniCard.Tests/Services/ListsViewModelTests.cs`. Use a small in-memory fake `IListService` and a minimal fake `IDecklistService` (3 methods). Reuse the `FakeCardService` copy pattern from `ListServiceTests`, or a `null!` card service if the test path doesn't call it.

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Views.Lists;
using Xunit;

namespace OmniCard.Tests.Services;

public class ListsViewModelTests
{
    [Fact]
    public void SetGame_LoadsListsForThatGame()
    {
        var svc = new FakeListService();
        svc.Seed(new CardList { Id = 1, Name = "A", Game = CardGame.Mtg });
        svc.Seed(new CardList { Id = 2, Name = "B", Game = CardGame.Pokemon });
        var vm = new ListsViewModel(svc, null!, new FakeDecklistService(), NullLogger<ListsViewModel>.Instance);

        vm.SetGame(CardGame.Mtg);

        Assert.Single(vm.Lists);
        Assert.Equal("A", vm.Lists[0].Name);
    }

    [Fact]
    public void CreateList_AddsAndSelects()
    {
        var svc = new FakeListService();
        var vm = new ListsViewModel(svc, null!, new FakeDecklistService(), NullLogger<ListsViewModel>.Instance);
        vm.SetGame(CardGame.Mtg);
        vm.NewListName = "My List";

        vm.CreateListCommand.Execute(null);

        Assert.Single(vm.Lists);
        Assert.Equal("My List", vm.SelectedList!.Name);
        Assert.Equal("", vm.NewListName);
    }

    [Fact]
    public void RunSummaryReport_BuildsResult_AndInvokesExport()
    {
        var svc = new FakeListService();
        var list = new CardList { Id = 1, Name = "L", Game = CardGame.Mtg };
        svc.Seed(list);
        svc.Items[1] = new List<CardListItem>
        {
            new() { Id = 1, CardListId = 1, Quantity = 1, CardName = "Sol Ring" },
        };
        var vm = new ListsViewModel(svc, null!, new FakeDecklistService(), NullLogger<ListsViewModel>.Instance);
        vm.SetGame(CardGame.Mtg);
        vm.SelectedList = vm.Lists[0];

        DecklistCheckResult? exported = null;
        vm.ExportPdf = r => exported = r;
        vm.RunSummaryReportCommand.Execute(null);

        Assert.NotNull(vm.Result);
        Assert.Same(vm.Result, exported);
    }

    private sealed class FakeListService : IListService
    {
        private readonly List<CardList> _lists = [];
        public Dictionary<int, List<CardListItem>> Items { get; } = new();
        private int _nextId = 100;
        public void Seed(CardList l) => _lists.Add(l);

        public IReadOnlyList<CardList> GetLists(CardGame game) => _lists.Where(l => l.Game == game).ToList();
        public CardList CreateList(string name, CardGame game)
        {
            var l = new CardList { Id = _nextId++, Name = name, Game = game };
            _lists.Add(l); return l;
        }
        public void RenameList(int listId, string name) { }
        public void DeleteList(int listId) => _lists.RemoveAll(l => l.Id == listId);
        public IReadOnlyList<CardListItem> GetItems(int listId) => Items.TryGetValue(listId, out var v) ? v : [];
        public CardListItem AddPrinting(int listId, CardMatch p, bool foil, int qty, ListItemSource s)
            => new() { CardListId = listId, CardName = p.Name, Quantity = qty };
        public void RemoveItem(int itemId) { }
        public void SetQuantity(int itemId, int quantity) { }
        public AddCardsResult AddCardsByName(int listId, IEnumerable<DecklistEntry> entries)
            => new(entries.Count(), []);
        public void RefreshPrices(int listId) { }
        public List<DecklistEntry> ToDecklistEntries(int listId)
            => GetItems(listId).Select(i => new DecklistEntry(i.Quantity, i.CardName, i.SetCode, i.CollectorNumber)).ToList();
    }

    private sealed class FakeDecklistService : IDecklistService
    {
        public Task<(string DeckName, List<DecklistEntry> Entries)?> FetchDecklistAsync(string url)
            => Task.FromResult<(string, List<DecklistEntry>)?>(null);
        public (string DeckName, List<DecklistEntry> Entries) ParseDecklistText(string text)
            => ("Pasted", []);
        public DecklistCheckResult CheckAgainstCollection(string deckName, string deckSource, List<DecklistEntry> entries, CardGame game)
            => new() { DeckName = deckName, DeckSource = deckSource, OwnedEntries = [], MissingEntries = [] };
    }
}
```

- [ ] **Step 3: Run the VM tests**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~ListsViewModelTests"`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add OmniCard/Views/Lists/ListsViewModel.cs OmniCard.Tests/Services/ListsViewModelTests.cs
git commit -m "feat(lists): ListsViewModel with CRUD/import/report commands"
```

---

## Task 7: ListsView + sidebar tab (UI, manual verification)

**Files:**
- Create: `OmniCard/Views/Lists/ListsView.xaml`, `OmniCard/Views/Lists/ListsView.xaml.cs`
- Modify: `OmniCard/Views/Root/RootView.xaml` (new `TabItem` after Sales)
- Modify: `OmniCard/Views/Root/RootView.xaml.cs` (optional lazy-load hook)

**Interfaces:**
- Consumes: `ListsViewModel` via `DataContext`; `RootViewModel.Lists`.

- [ ] **Step 1: Create the View**

Create `OmniCard/Views/Lists/ListsView.xaml` as a `UserControl`. Follow the theme constraints (explicit `Foreground` on text). Two-column layout: left = lists + create/delete; right = items grid + add/import/paste + report buttons. Bind everything to the injected `ListsViewModel` exposed as the control's `DataContext` (set in code-behind). Minimum functional markup:

```xml
<UserControl x:Class="OmniCard.Views.Lists.ListsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:conv="clr-namespace:OmniCard.Controls.Converters;assembly=OmniCard.Controls"
             TextElement.Foreground="{DynamicResource MaterialDesign.Brush.Foreground}">
    <Grid Margin="12">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="240"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>

        <!-- Left: lists -->
        <DockPanel Grid.Column="0" Margin="0,0,12,0">
            <DockPanel DockPanel.Dock="Top" Margin="0,0,0,8">
                <Button DockPanel.Dock="Right" Content="Create" Margin="8,0,0,0"
                        Command="{Binding CreateListCommand}"/>
                <TextBox Text="{Binding NewListName, UpdateSourceTrigger=PropertyChanged}"
                         Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"/>
            </DockPanel>
            <Button DockPanel.Dock="Bottom" Content="Delete List" Margin="0,8,0,0"
                    Command="{Binding DeleteListCommand}"/>
            <ListBox ItemsSource="{Binding Lists}" SelectedItem="{Binding SelectedList}"
                     DisplayMemberPath="Name"/>
        </DockPanel>

        <!-- Right: selected list -->
        <Grid Grid.Column="1">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
                <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>

            <!-- Add / import / paste -->
            <StackPanel Grid.Row="0">
                <DockPanel Margin="0,0,0,6">
                    <Button DockPanel.Dock="Right" Content="Search" Margin="8,0,0,0"
                            Command="{Binding SearchCommand}"/>
                    <Button DockPanel.Dock="Right" Content="Add" Margin="8,0,0,0"
                            Command="{Binding AddSelectedPrintingCommand}"/>
                    <TextBox Text="{Binding SearchQuery, UpdateSourceTrigger=PropertyChanged}"
                             Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"/>
                </DockPanel>
                <ListBox ItemsSource="{Binding SearchResults}" SelectedItem="{Binding SelectedSearchResult}"
                         MaxHeight="120">
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <TextBlock Foreground="{DynamicResource MaterialDesign.Brush.Foreground}">
                                <Run Text="{Binding Name, Mode=OneWay}"/>
                                <Run Text="{Binding SetCode, Mode=OneWay}"/>
                            </TextBlock>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
                <DockPanel Margin="0,6,0,0"
                           Visibility="{Binding CanImportUrl, Converter={conv:BoolToVisibilityConverter}}">
                    <Button DockPanel.Dock="Right" Content="Import URL" Margin="8,0,0,0"
                            Command="{Binding ImportUrlCommand}"/>
                    <TextBox Text="{Binding ImportUrl, UpdateSourceTrigger=PropertyChanged}"
                             Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"/>
                </DockPanel>
                <DockPanel Margin="0,6,0,0">
                    <Button DockPanel.Dock="Right" Content="Add Pasted" Margin="8,0,0,0"
                            VerticalAlignment="Top" Command="{Binding ParsePasteCommand}"/>
                    <TextBox Text="{Binding PasteText, UpdateSourceTrigger=PropertyChanged}"
                             AcceptsReturn="True" Height="60" TextWrapping="Wrap"
                             VerticalScrollBarVisibility="Auto"
                             Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"/>
                </DockPanel>
            </StackPanel>

            <CheckBox Grid.Row="1" Margin="0,8" Content="Ignore basic lands (in reports)"
                      IsChecked="{Binding IgnoreBasicLands}"
                      Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"/>

            <!-- Items -->
            <DataGrid Grid.Row="2" ItemsSource="{Binding Items}" SelectedItem="{Binding SelectedItem}"
                      AutoGenerateColumns="False" IsReadOnly="True" CanUserAddRows="False">
                <DataGrid.Columns>
                    <DataGridTextColumn Header="Qty" Binding="{Binding Quantity}" Width="50"/>
                    <DataGridTextColumn Header="Name" Binding="{Binding CardName}" Width="*"/>
                    <DataGridTextColumn Header="Set" Binding="{Binding SetCode}" Width="70"/>
                    <DataGridCheckBoxColumn Header="Foil" Binding="{Binding IsFoil}" Width="50"/>
                    <DataGridTextColumn Header="Price" Binding="{Binding AddedMarketPrice, StringFormat=C}" Width="80"/>
                    <DataGridCheckBoxColumn Header="Unpriced" Binding="{Binding IsUnpriced}" Width="70"/>
                </DataGrid.Columns>
            </DataGrid>

            <!-- Actions + status -->
            <DockPanel Grid.Row="3" Margin="0,8,0,0">
                <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                    <Button Content="Remove" Margin="0,0,8,0" Command="{Binding RemoveSelectedItemCommand}"/>
                    <Button Content="Refresh Prices" Margin="0,0,8,0" Command="{Binding RefreshPricesCommand}"/>
                    <Button Content="Summary Report" Margin="0,0,8,0" Command="{Binding RunSummaryReportCommand}"/>
                    <Button Content="Detailed Report" Command="{Binding RunDetailedReportCommand}"/>
                </StackPanel>
                <TextBlock Text="{Binding StatusMessage}" VerticalAlignment="Center" TextWrapping="Wrap"
                           Foreground="{DynamicResource MaterialDesign.Brush.Primary}"/>
            </DockPanel>
        </Grid>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Create the code-behind (DataContext + PDF export callbacks)**

Create `OmniCard/Views/Lists/ListsView.xaml.cs`, mirroring `DecklistCheckView.xaml.cs`'s SaveFileDialog export wiring. The `ListsViewModel` is resolved from DI (the control is hosted in the tab; get the VM from `RootViewModel.Lists` via the DataContext, or resolve directly). Bind `ExportPdf`/`ExportDetailedPdf`:

```csharp
using System.Windows.Controls;
using Microsoft.Win32;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.Lists;

public partial class ListsView : UserControl
{
    private readonly IDecklistPdfExporter _exporter;

    public ListsView(ListsViewModel viewModel, IDecklistPdfExporter exporter)
    {
        InitializeComponent();
        _exporter = exporter;
        DataContext = viewModel;
        viewModel.ExportPdf = r => Save(r, detailed: false);
        viewModel.ExportDetailedPdf = r => Save(r, detailed: true);
    }

    private void Save(DecklistCheckResult result, bool detailed)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = $"{result.DeckName}{(detailed ? "-detailed" : "")}.pdf",
        };
        if (dlg.ShowDialog() != true) return;
        if (detailed) _exporter.ExportDetailed(result, dlg.FileName);
        else _exporter.Export(result, dlg.FileName);
    }
}
```

> Verify the exact `IDecklistPdfExporter.Export`/`ExportDetailed` parameter order against `OmniCard/Views/DecklistCheck/DecklistCheckView.xaml.cs:16-44` and match it. Register `ListsView` as transient in `App.xaml.cs` if the tab hosts it via DI; if the tab instantiates it directly in XAML, instead set `DataContext` through the tab binding and resolve the VM from `ViewModel.Lists` (see Step 3).

- [ ] **Step 3: Add the sidebar tab**

In `OmniCard/Views/Root/RootView.xaml`, after the Sales `TabItem` (~line 303, index 3), add a new `TabItem` (index 4) following the existing header pattern (a `StackPanel` with a `materialDesign:PackIcon` + a label `TextBlock` whose visibility binds to `ViewModel.IsSidebarExpanded`). Host the view and pass the VM:

```xml
        <TabItem>
            <TabItem.Header>
                <StackPanel Orientation="Horizontal">
                    <materialDesign:PackIcon Kind="FormatListBulleted" Width="24" Height="24"/>
                    <TextBlock Text="Lists" Margin="12,0,0,0" VerticalAlignment="Center"
                               Visibility="{Binding ViewModel.IsSidebarExpanded, Converter={conv:BoolToVisibilityConverter}}"/>
                </StackPanel>
            </TabItem.Header>
            <lists:ListsView DataContext="{Binding ViewModel.Lists}"/>
        </TabItem>
```

Add the namespace to the `Window`/root element: `xmlns:lists="clr-namespace:OmniCard.Views.Lists"`. Match the exact icon-kind/converter/`materialDesign` alias already used by the sibling tabs (copy their header markup and change the icon + label).

> If `ListsView`'s constructor requires DI (Step 2), it can't be instantiated directly in XAML with a parameterless constructor. Simplest path that matches this codebase: give `ListsView` a parameterless constructor that resolves `ListsViewModel` + `IDecklistPdfExporter` from `App.Services` (see how other views obtain services), OR host the view without constructor injection and set only `DataContext="{Binding ViewModel.Lists}"`, wiring the export callbacks in a `Loaded` handler. Pick whichever matches the dominant pattern for tab-hosted views in `RootView.xaml`; verify by checking how `SalesView` is constructed.

- [ ] **Step 4: Optional lazy-load hook**

If list loading should happen on tab activation (rather than on game change), add an `else if (MainTabControl.SelectedItem == tabItemLists)` branch in `OmniCard/Views/Root/RootView.xaml.cs:32-45` calling `viewModel.Lists.SetGame(viewModel.SelectedGame)`, and give the new `TabItem` `x:Name="tabItemLists"`. Not required — `SetGame` already fires via `OnSelectedGameChanged`; add this only if the tab shows stale data on first open.

- [ ] **Step 5: Build**

Run: `dotnet build OmniCard/OmniCard.csproj`
Expected: SUCCESS (0 errors).

- [ ] **Step 6: Manual verification (WPF has no automatable UI surface)**

Launch the app. Verify each workflow step:
1. Select **MTG** in the game filter → open the **Lists** tab.
2. Type a name, click **Create** → the list appears and is selected.
3. **Add a card:** type a card name, **Search**, select a printing, **Add** → it appears in the grid with a price.
4. **Import URL:** paste a Moxfield/Archidekt deck URL, **Import URL** → cards populate (cheapest printings); confirm the URL row is visible for MTG.
5. **Paste:** paste the `decklist.txt` sample, **Add Pasted** → cards populate; a status line reports any unresolved names.
6. **Summary Report** / **Detailed Report** → a Save dialog appears; the PDF exports with owned/missing/cost.
7. Switch the game filter to a non-MTG game → the list panel shows that game's lists, and the **Import URL** row is hidden.
8. Confirm text is readable in dark theme (no near-black text).

- [ ] **Step 7: Commit**

```bash
git add OmniCard/Views/Lists/ListsView.xaml OmniCard/Views/Lists/ListsView.xaml.cs OmniCard/Views/Root/RootView.xaml OmniCard/Views/Root/RootView.xaml.cs
git commit -m "feat(lists): Lists sidebar tab view"
```

---

## Self-Review

**Spec coverage:**
- Select game + Lists tab → Task 5 (`SetGame` hook) + Task 7 (tab). ✓
- Create a list → Tasks 2, 6, 7. ✓
- Add a card (specific printing) → Task 2 `AddPrinting` + Task 6 search/add + Task 7 UI. ✓
- URL import (MTG only) → Task 6 `ImportUrlAsync` + `CanImportUrl` + Task 7 conditional row. ✓
- Paste + cheapest → Tasks 3 (`AddCardsByName`/`ResolveCheapest`) + 6 `ParsePaste`. ✓
- Reports anytime → Task 4 (game param) + Task 6 report commands + Task 7 buttons/export. ✓
- Cheapest = cheapest non-foil; fallback first-printing flagged unpriced → Task 3 `ResolveCheapest`. ✓
- Frozen printing + manual refresh → Task 1 (frozen fields) + Task 3 `RefreshPrices`. ✓
- Want-list, no inventory mutation → confirmed: `ListService` never touches `Lots`/`Movements`. ✓
- Multiple named lists per game; add-existing increments; paste merges → Tasks 2/3 merge logic. ✓
- Basic lands added, ignored only in reports → Task 6 `IgnoreBasicLands` applied in `BuildResult`. ✓
- Persistence via DbContext + UnifiedMigrationService, no EF migrations, decimals as TEXT → Task 1. ✓

**Placeholder scan:** No TBD/TODO. The two "verify against existing file" notes in Task 7 (exporter signature, tab-hosting pattern) are deliberate cross-checks against concrete files, not deferred work.

**Type consistency:** `IListService` signatures in Task 2 match calls in Task 6; `AddCardsResult(int AddedCount, IReadOnlyList<string> UnresolvedNames)` used consistently; `CheckAgainstCollection(..., CardGame game)` defined in Task 4 and called in Task 6; `ListItemSource { Manual, Url, Paste }` used in Tasks 1/2/3/6; `CardMatch` property names (`Name`, `GameSpecificId`, `SetCode`, `CollectorNumber`) match `OmniCard.Shared/Models/CardMatch.cs`.

**Execution-order note:** Tasks 6 and 5 are compile-interdependent — implement **Task 6 (ViewModel) before Task 5 (wiring)**, then Task 7. This is called out in Task 5's header.
