# Sealed Product Rework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rework sealed product entry to support ~28 MTG product types across all eras, with archetype-driven auto-fill and a scan-first bulk entry dialog.

**Architecture:** Expand `SealedProductType` enum, add a static `SealedProductArchetypeRegistry` that maps each type to name patterns/default contents/tier, replace the `AddSealedProductView` with a stay-open `SealedProductEntryView` optimized for UPC scanning. Existing template/instance DB schema is unchanged; only the `BundleBox` enum value gets renamed to `Bundle`.

**Tech Stack:** WPF (.NET 10), EF Core 10 + SQLite, CommunityToolkit.Mvvm, MaterialDesignThemes

## Global Constraints

- Target framework: `net10.0-windows10.0.22621.0`
- MVVM pattern: ViewModels extend `OmniCard.Views.ViewModel` (which extends `ObservableObject`), use `[ObservableProperty]` and `[RelayCommand]` source generators
- Views implement `IView<TViewModel>` interface, set `DataContext = this`, expose `ViewModel` property
- Dialogs use `CloseDialog` action pattern: `Action<bool>? CloseDialog` set in code-behind, invoked with `true`/`false`
- DB uses `IDbContextFactory<SealedProductDbContext>` — create short-lived contexts per operation
- Enum values stored as strings in SQLite via `.HasConversion<string>()`
- Tests use in-memory SQLite via `SqliteConnection("Data Source=:memory:")`
- All sealed product files live under `OmniCard/Models/`, `OmniCard/Data/`, `OmniCard/Services/`, `OmniCard/Views/Root/`, and `OmniCard/Views/SealedProductEditor/`

---

### Task 1: Expand SealedProductType Enum and Add ArchetypeTier

**Files:**
- Modify: `OmniCard/Models/SealedProductType.cs`
- Create: `OmniCard/Models/ArchetypeTier.cs`

**Interfaces:**
- Produces: `SealedProductType` enum with 28 values; `ArchetypeTier` enum with 7 values — used by every subsequent task

- [ ] **Step 1: Replace SealedProductType enum**

Replace the entire contents of `OmniCard/Models/SealedProductType.cs`:

```csharp
namespace OmniCard.Models;

public enum SealedProductType
{
    // Cases
    Case,

    // Boxes
    PlayBoosterBox,
    DraftBoosterBox,
    SetBoosterBox,
    CollectorBoosterBox,
    ThemeBoosterBox,
    BoosterBox,

    // Packs
    PlayBoosterPack,
    DraftBoosterPack,
    SetBoosterPack,
    CollectorBoosterPack,
    ThemeBoosterPack,
    BoosterPack,
    PromoPack,

    // Bundles & Kits
    Bundle,
    GiftBundle,
    FatPack,
    PrereleaseKit,
    StarterKit,

    // Decks & Fixed Products
    CommanderDeck,
    PlaneswalkerDeck,
    IntroPack,
    ThemeDeck,
    IntroDeck,
    WelcomeDeck,
    FixedPack,

    // Special Products
    SecretLair,
    FromTheVault,
    BlisterPack,

    // Terminal
    Card,
}
```

- [ ] **Step 2: Create ArchetypeTier enum**

Create `OmniCard/Models/ArchetypeTier.cs`:

```csharp
namespace OmniCard.Models;

public enum ArchetypeTier
{
    Case,
    Box,
    Pack,
    Deck,
    Kit,
    Special,
    Card,
}
```

- [ ] **Step 3: Build and verify compilation**

Run: `dotnet build OmniCard/OmniCard.csproj`
Expected: Build errors in files that reference removed enum values (`BundleBox`, `CollectorBoosterPack` in switch statements). This is expected — those are fixed in Task 3 and Task 4. Verify the enum files themselves compiled.

- [ ] **Step 4: Commit**

```bash
git add OmniCard/Models/SealedProductType.cs OmniCard/Models/ArchetypeTier.cs
git commit -m "feat: expand SealedProductType enum to 28 types and add ArchetypeTier"
```

---

### Task 2: Create SealedProductArchetypeRegistry

**Files:**
- Create: `OmniCard/Models/SealedProductArchetype.cs`
- Create: `OmniCard/Services/SealedProductArchetypeRegistry.cs`
- Create: `OmniCard.Tests/Services/SealedProductArchetypeRegistryTests.cs`

**Interfaces:**
- Consumes: `SealedProductType`, `ArchetypeTier` from Task 1
- Produces: `SealedProductArchetype` record with `NamePattern`, `DefaultContents`, `Tier`; `SealedProductArchetypeRegistry` static class with `GetArchetype()`, `GetDisplayName()`, `GetTier()`, `GenerateTemplateName()`, `All` property

- [ ] **Step 1: Create SealedProductArchetype record**

Create `OmniCard/Models/SealedProductArchetype.cs`:

```csharp
namespace OmniCard.Models;

/// <summary>
/// Defines the default structure for a sealed product type:
/// how it's named, what it contains, and its tier in the product hierarchy.
/// </summary>
public record SealedProductArchetype(
    string NamePattern,
    List<ArchetypeContent> DefaultContents,
    ArchetypeTier Tier
);

public record ArchetypeContent(int Quantity, SealedProductType ChildType);
```

- [ ] **Step 2: Write failing tests for the archetype registry**

Create `OmniCard.Tests/Services/SealedProductArchetypeRegistryTests.cs`:

```csharp
using OmniCard.Models;
using OmniCard.Services;

namespace OmniCard.Tests.Services;

public class SealedProductArchetypeRegistryTests
{
    [Fact]
    public void AllSealedProductTypes_HaveArchetypes()
    {
        foreach (var type in Enum.GetValues<SealedProductType>())
        {
            var archetype = SealedProductArchetypeRegistry.GetArchetype(type);
            Assert.NotNull(archetype);
        }
    }

    [Fact]
    public void GetDisplayName_ReturnsHumanReadable()
    {
        Assert.Equal("Play Booster Box", SealedProductArchetypeRegistry.GetDisplayName(SealedProductType.PlayBoosterBox));
        Assert.Equal("Collector Booster Pack", SealedProductArchetypeRegistry.GetDisplayName(SealedProductType.CollectorBoosterPack));
        Assert.Equal("Commander Deck", SealedProductArchetypeRegistry.GetDisplayName(SealedProductType.CommanderDeck));
        Assert.Equal("Fat Pack", SealedProductArchetypeRegistry.GetDisplayName(SealedProductType.FatPack));
        Assert.Equal("Card", SealedProductArchetypeRegistry.GetDisplayName(SealedProductType.Card));
    }

    [Fact]
    public void GetTier_ReturnsCorrectTiers()
    {
        Assert.Equal(ArchetypeTier.Case, SealedProductArchetypeRegistry.GetTier(SealedProductType.Case));
        Assert.Equal(ArchetypeTier.Box, SealedProductArchetypeRegistry.GetTier(SealedProductType.PlayBoosterBox));
        Assert.Equal(ArchetypeTier.Pack, SealedProductArchetypeRegistry.GetTier(SealedProductType.BoosterPack));
        Assert.Equal(ArchetypeTier.Deck, SealedProductArchetypeRegistry.GetTier(SealedProductType.CommanderDeck));
        Assert.Equal(ArchetypeTier.Kit, SealedProductArchetypeRegistry.GetTier(SealedProductType.Bundle));
        Assert.Equal(ArchetypeTier.Special, SealedProductArchetypeRegistry.GetTier(SealedProductType.SecretLair));
        Assert.Equal(ArchetypeTier.Card, SealedProductArchetypeRegistry.GetTier(SealedProductType.Card));
    }

    [Fact]
    public void GenerateTemplateName_SubstitutesSetName()
    {
        var name = SealedProductArchetypeRegistry.GenerateTemplateName(SealedProductType.PlayBoosterBox, "Modern Horizons 3");
        Assert.Equal("Modern Horizons 3 Play Booster Box", name);
    }

    [Fact]
    public void GenerateTemplateName_NullSetName_UsesGeneric()
    {
        var name = SealedProductArchetypeRegistry.GenerateTemplateName(SealedProductType.PlayBoosterBox, null);
        Assert.Equal("Generic Play Booster Box", name);
    }

    [Fact]
    public void GenerateTemplateName_SecretLair_UsesColonFormat()
    {
        var name = SealedProductArchetypeRegistry.GenerateTemplateName(SealedProductType.SecretLair, "Featuring: Bloodghast");
        Assert.Equal("Secret Lair: Featuring: Bloodghast", name);
    }

    [Fact]
    public void PlayBoosterBox_HasCorrectDefaults()
    {
        var archetype = SealedProductArchetypeRegistry.GetArchetype(SealedProductType.PlayBoosterBox);
        Assert.Equal(ArchetypeTier.Box, archetype.Tier);
        Assert.Single(archetype.DefaultContents);
        Assert.Equal(36, archetype.DefaultContents[0].Quantity);
        Assert.Equal(SealedProductType.PlayBoosterPack, archetype.DefaultContents[0].ChildType);
    }

    [Fact]
    public void PrereleaseKit_HasMultipleContentLines()
    {
        var archetype = SealedProductArchetypeRegistry.GetArchetype(SealedProductType.PrereleaseKit);
        Assert.Equal(ArchetypeTier.Kit, archetype.Tier);
        Assert.Equal(2, archetype.DefaultContents.Count);
        Assert.Contains(archetype.DefaultContents, c => c.ChildType == SealedProductType.PlayBoosterPack && c.Quantity == 6);
        Assert.Contains(archetype.DefaultContents, c => c.ChildType == SealedProductType.PromoPack && c.Quantity == 1);
    }

    [Fact]
    public void LeafTypes_HaveEmptyContents()
    {
        var leafTypes = new[]
        {
            SealedProductType.PlayBoosterPack, SealedProductType.DraftBoosterPack,
            SealedProductType.SetBoosterPack, SealedProductType.CollectorBoosterPack,
            SealedProductType.ThemeBoosterPack, SealedProductType.BoosterPack,
            SealedProductType.PromoPack, SealedProductType.FixedPack, SealedProductType.Card,
        };

        foreach (var type in leafTypes)
        {
            var archetype = SealedProductArchetypeRegistry.GetArchetype(type);
            Assert.Empty(archetype.DefaultContents);
        }
    }

    [Fact]
    public void GetTypesGroupedByTier_ReturnsAllTypes()
    {
        var grouped = SealedProductArchetypeRegistry.GetTypesGroupedByTier();
        var allTypes = grouped.SelectMany(g => g).ToList();
        var enumValues = Enum.GetValues<SealedProductType>().ToList();
        Assert.Equal(enumValues.Count, allTypes.Count);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~SealedProductArchetypeRegistryTests" --no-restore`
Expected: Build failure — `SealedProductArchetypeRegistry` does not exist yet.

- [ ] **Step 4: Implement SealedProductArchetypeRegistry**

Create `OmniCard/Services/SealedProductArchetypeRegistry.cs`:

```csharp
using OmniCard.Models;

namespace OmniCard.Services;

public static class SealedProductArchetypeRegistry
{
    private static readonly Dictionary<SealedProductType, SealedProductArchetype> Archetypes = new()
    {
        // Cases
        [SealedProductType.Case] = new("{Set} Case", [], ArchetypeTier.Case),

        // Boxes
        [SealedProductType.PlayBoosterBox] = new("{Set} Play Booster Box",
            [new(36, SealedProductType.PlayBoosterPack)], ArchetypeTier.Box),
        [SealedProductType.DraftBoosterBox] = new("{Set} Draft Booster Box",
            [new(36, SealedProductType.DraftBoosterPack)], ArchetypeTier.Box),
        [SealedProductType.SetBoosterBox] = new("{Set} Set Booster Box",
            [new(30, SealedProductType.SetBoosterPack)], ArchetypeTier.Box),
        [SealedProductType.CollectorBoosterBox] = new("{Set} Collector Booster Box",
            [new(12, SealedProductType.CollectorBoosterPack)], ArchetypeTier.Box),
        [SealedProductType.ThemeBoosterBox] = new("{Set} Theme Booster Box",
            [new(12, SealedProductType.ThemeBoosterPack)], ArchetypeTier.Box),
        [SealedProductType.BoosterBox] = new("{Set} Booster Box",
            [new(36, SealedProductType.BoosterPack)], ArchetypeTier.Box),

        // Packs (leaf nodes — no contents)
        [SealedProductType.PlayBoosterPack] = new("{Set} Play Booster Pack", [], ArchetypeTier.Pack),
        [SealedProductType.DraftBoosterPack] = new("{Set} Draft Booster Pack", [], ArchetypeTier.Pack),
        [SealedProductType.SetBoosterPack] = new("{Set} Set Booster Pack", [], ArchetypeTier.Pack),
        [SealedProductType.CollectorBoosterPack] = new("{Set} Collector Booster Pack", [], ArchetypeTier.Pack),
        [SealedProductType.ThemeBoosterPack] = new("{Set} Theme Booster Pack", [], ArchetypeTier.Pack),
        [SealedProductType.BoosterPack] = new("{Set} Booster Pack", [], ArchetypeTier.Pack),
        [SealedProductType.PromoPack] = new("{Set} Promo Pack", [], ArchetypeTier.Pack),

        // Bundles & Kits
        [SealedProductType.Bundle] = new("{Set} Bundle",
            [new(8, SealedProductType.PlayBoosterPack)], ArchetypeTier.Kit),
        [SealedProductType.GiftBundle] = new("{Set} Gift Bundle",
            [new(10, SealedProductType.PlayBoosterPack)], ArchetypeTier.Kit),
        [SealedProductType.FatPack] = new("{Set} Fat Pack",
            [new(9, SealedProductType.BoosterPack)], ArchetypeTier.Kit),
        [SealedProductType.PrereleaseKit] = new("{Set} Prerelease Kit",
            [new(6, SealedProductType.PlayBoosterPack), new(1, SealedProductType.PromoPack)], ArchetypeTier.Kit),
        [SealedProductType.StarterKit] = new("{Set} Starter Kit",
            [new(2, SealedProductType.FixedPack)], ArchetypeTier.Kit),

        // Decks
        [SealedProductType.CommanderDeck] = new("{Set} Commander Deck",
            [new(1, SealedProductType.FixedPack)], ArchetypeTier.Deck),
        [SealedProductType.PlaneswalkerDeck] = new("{Set} Planeswalker Deck",
            [new(1, SealedProductType.FixedPack), new(1, SealedProductType.BoosterPack)], ArchetypeTier.Deck),
        [SealedProductType.IntroPack] = new("{Set} Intro Pack",
            [new(1, SealedProductType.FixedPack), new(2, SealedProductType.BoosterPack)], ArchetypeTier.Deck),
        [SealedProductType.ThemeDeck] = new("{Set} Theme Deck",
            [new(1, SealedProductType.FixedPack)], ArchetypeTier.Deck),
        [SealedProductType.IntroDeck] = new("{Set} Intro Deck",
            [new(1, SealedProductType.FixedPack)], ArchetypeTier.Deck),
        [SealedProductType.WelcomeDeck] = new("{Set} Welcome Deck",
            [new(1, SealedProductType.FixedPack)], ArchetypeTier.Deck),
        [SealedProductType.FixedPack] = new("{Set} Fixed Pack", [], ArchetypeTier.Pack),

        // Special
        [SealedProductType.SecretLair] = new("Secret Lair: {Set}",
            [new(1, SealedProductType.FixedPack)], ArchetypeTier.Special),
        [SealedProductType.FromTheVault] = new("From the Vault: {Set}",
            [new(1, SealedProductType.FixedPack)], ArchetypeTier.Special),
        [SealedProductType.BlisterPack] = new("{Set} Blister Pack",
            [new(3, SealedProductType.BoosterPack)], ArchetypeTier.Pack),

        // Terminal
        [SealedProductType.Card] = new("{Set} Card", [], ArchetypeTier.Card),
    };

    public static SealedProductArchetype GetArchetype(SealedProductType type) =>
        Archetypes[type];

    public static string GetDisplayName(SealedProductType type) =>
        Archetypes[type].NamePattern
            .Replace("{Set} ", "")
            .Replace("{Set}", "")
            .Replace("Secret Lair: ", "Secret Lair")
            .Replace("From the Vault: ", "From the Vault")
            .Trim() switch
        {
            "" => type.ToString(),
            var name => name
        };

    public static ArchetypeTier GetTier(SealedProductType type) =>
        Archetypes[type].Tier;

    public static string GenerateTemplateName(SealedProductType type, string? setName)
    {
        var name = setName ?? "Generic";
        return Archetypes[type].NamePattern.Replace("{Set}", name);
    }

    public static IReadOnlyList<IGrouping<ArchetypeTier, SealedProductType>> GetTypesGroupedByTier() =>
        Archetypes
            .GroupBy(kv => kv.Value.Tier, kv => kv.Key)
            .OrderBy(g => g.Key)
            .ToList();
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~SealedProductArchetypeRegistryTests" --no-restore`
Expected: All 9 tests pass.

- [ ] **Step 6: Commit**

```bash
git add OmniCard/Models/SealedProductArchetype.cs OmniCard/Services/SealedProductArchetypeRegistry.cs OmniCard.Tests/Services/SealedProductArchetypeRegistryTests.cs
git commit -m "feat: add SealedProductArchetypeRegistry with defaults for all 28 product types"
```

---

### Task 3: DB Migration and Fix Compilation Errors from Enum Rename

**Files:**
- Modify: `OmniCard/App.xaml.cs` (add migration logic near line 210)
- Modify: `OmniCard/Services/SealedProductService.cs:233-244` (update `FormatProductType` switch)
- Modify: `OmniCard/Views/Root/SealedProductViewModel.cs` (no changes needed — uses generic `SealedProductType` references)
- Modify: `OmniCard.Tests/Services/SealedProductServiceTests.cs` (update `BundleBox` references)
- Modify: `OmniCard.Tests/Data/SealedProductDbContextTests.cs` (update `BundleBox` references)

**Interfaces:**
- Consumes: `SealedProductType` from Task 1 (the enum no longer has `BundleBox`)
- Produces: All existing code compiles against the new enum; DB migration renames stored `BundleBox` → `Bundle` strings

- [ ] **Step 1: Update FormatProductType in SealedProductService**

In `OmniCard/Services/SealedProductService.cs`, replace the `FormatProductType` method (lines 233-244) with a version that delegates to the archetype registry:

```csharp
    private static string FormatProductType(SealedProductType type) =>
        SealedProductArchetypeRegistry.GetDisplayName(type);
```

Add the using at the top of the file if not already present — but `SealedProductArchetypeRegistry` is in the same `OmniCard.Services` namespace, so no new using is needed.

- [ ] **Step 2: Add DB migration in App.xaml.cs**

In `OmniCard/App.xaml.cs`, after the `sealedCtx.Database.EnsureCreated();` block (around line 211), add the migration call:

```csharp
            // Migrate BundleBox → Bundle enum values
            MigrateSealedProductEnumValues(sealedCtx);
```

Then add a new private static method in the `App` class:

```csharp
    private static void MigrateSealedProductEnumValues(SealedProductDbContext ctx)
    {
        ctx.Database.ExecuteSqlRaw(
            "UPDATE Templates SET ProductType = 'Bundle' WHERE ProductType = 'BundleBox'");
        ctx.Database.ExecuteSqlRaw(
            "UPDATE TemplateContents SET ChildProductType = 'Bundle' WHERE ChildProductType = 'BundleBox'");
    }
```

Note: Move the `sealedCtx.Database.EnsureCreated()` call outside the `using` block's immediate disposal, or restructure so the context is still alive for migration. The simplest change: keep the existing `using` block and add the migration call before the closing brace:

```csharp
            using (var sealedCtx = Host.Services.GetRequiredService<IDbContextFactory<SealedProductDbContext>>().CreateDbContext())
            {
                sealedCtx.Database.EnsureCreated();
                MigrateSealedProductEnumValues(sealedCtx);
            }
```

- [ ] **Step 3: Update test files — replace BundleBox with Bundle**

In `OmniCard.Tests/Services/SealedProductServiceTests.cs`, find all occurrences of `SealedProductType.BundleBox` and replace with `SealedProductType.Bundle`. There are two occurrences:
- Line 126: `ProductType = SealedProductType.BundleBox,` → `ProductType = SealedProductType.Bundle,`
- Line 173: `ProductType = SealedProductType.BundleBox,` → `ProductType = SealedProductType.Bundle,`

In `OmniCard.Tests/Data/SealedProductDbContextTests.cs`, find all occurrences of `SealedProductType.BundleBox` and replace with `SealedProductType.Bundle`. There is one occurrence:
- Line 89: `ProductType = SealedProductType.BundleBox,` → `ProductType = SealedProductType.Bundle,`

- [ ] **Step 4: Build the entire solution**

Run: `dotnet build`
Expected: Clean build with no errors. All references to the old `BundleBox` enum value have been updated.

- [ ] **Step 5: Run all existing tests**

Run: `dotnet test OmniCard.Tests`
Expected: All tests pass (both existing and new archetype registry tests).

- [ ] **Step 6: Commit**

```bash
git add OmniCard/Services/SealedProductService.cs OmniCard/App.xaml.cs OmniCard.Tests/Services/SealedProductServiceTests.cs OmniCard.Tests/Data/SealedProductDbContextTests.cs
git commit -m "fix: migrate BundleBox enum to Bundle and fix all compilation errors"
```

---

### Task 4: Add CreateTemplateFromArchetype to SealedProductService

**Files:**
- Modify: `OmniCard/Services/SealedProductService.cs` (add new method to interface and implementation)
- Modify: `OmniCard.Tests/Services/SealedProductServiceTests.cs` (add tests)

**Interfaces:**
- Consumes: `SealedProductArchetypeRegistry.GenerateTemplateName()`, `SealedProductArchetypeRegistry.GetArchetype()` from Task 2
- Produces: `ISealedProductService.CreateTemplateFromArchetype(SealedProductType type, string? setCode, string? setName, string? upc)` — returns a fully populated `SealedProductTemplate`

- [ ] **Step 1: Write failing tests**

Add to `OmniCard.Tests/Services/SealedProductServiceTests.cs`:

```csharp
    [Fact]
    public void CreateTemplateFromArchetype_GeneratesCorrectTemplate()
    {
        var service = CreateService();
        var template = service.CreateTemplateFromArchetype(
            SealedProductType.PlayBoosterBox, "mh3", "Modern Horizons 3", null);

        Assert.Equal("Modern Horizons 3 Play Booster Box", template.Name);
        Assert.Equal("mh3", template.SetCode);
        Assert.Equal(SealedProductType.PlayBoosterBox, template.ProductType);
        Assert.Null(template.Upc);
        Assert.Single(template.Contents);
        Assert.Equal(36, template.Contents[0].Quantity);
        Assert.Equal(SealedProductType.PlayBoosterPack, template.Contents[0].ChildProductType);
    }

    [Fact]
    public void CreateTemplateFromArchetype_WithUpc_StoresUpc()
    {
        var service = CreateService();
        var template = service.CreateTemplateFromArchetype(
            SealedProductType.Bundle, "blb", "Bloomburrow", "195166253077");

        Assert.Equal("Bloomburrow Bundle", template.Name);
        Assert.Equal("195166253077", template.Upc);
    }

    [Fact]
    public void CreateTemplateFromArchetype_MultipleContents_AllPersisted()
    {
        var service = CreateService();
        var template = service.CreateTemplateFromArchetype(
            SealedProductType.PrereleaseKit, "mkm", "Murders at Karlov Manor", null);

        Assert.Equal("Murders at Karlov Manor Prerelease Kit", template.Name);
        Assert.Equal(2, template.Contents.Count);
        Assert.Contains(template.Contents, c => c.Quantity == 6 && c.ChildProductType == SealedProductType.PlayBoosterPack);
        Assert.Contains(template.Contents, c => c.Quantity == 1 && c.ChildProductType == SealedProductType.PromoPack);
    }

    [Fact]
    public void CreateTemplateFromArchetype_NullSetName_UsesGeneric()
    {
        var service = CreateService();
        var template = service.CreateTemplateFromArchetype(
            SealedProductType.BoosterPack, null, null, null);

        Assert.Equal("Generic Booster Pack", template.Name);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~SealedProductServiceTests.CreateTemplateFromArchetype" --no-restore`
Expected: Build failure — `CreateTemplateFromArchetype` does not exist on the interface.

- [ ] **Step 3: Add method to ISealedProductService interface and implement**

In `OmniCard/Services/SealedProductService.cs`, add to the `ISealedProductService` interface (after line 12):

```csharp
    SealedProductTemplate CreateTemplateFromArchetype(SealedProductType type, string? setCode, string? setName, string? upc);
```

Add the implementation in `SealedProductService` class (after the `CreateTemplate` method, around line 51):

```csharp
    public SealedProductTemplate CreateTemplateFromArchetype(SealedProductType type, string? setCode, string? setName, string? upc)
    {
        var archetype = SealedProductArchetypeRegistry.GetArchetype(type);
        var template = new SealedProductTemplate
        {
            Name = SealedProductArchetypeRegistry.GenerateTemplateName(type, setName),
            SetCode = setCode,
            Upc = string.IsNullOrWhiteSpace(upc) ? null : upc.Trim(),
            ProductType = type,
            Contents = archetype.DefaultContents.Select(c => new SealedProductContents
            {
                Quantity = c.Quantity,
                ChildProductType = c.ChildType,
            }).ToList(),
        };

        return CreateTemplate(template);
    }
```

- [ ] **Step 4: Also update GetOrCreateGenericTemplate to use archetype names**

In `OmniCard/Services/SealedProductService.cs`, update the `GetOrCreateGenericTemplate` method (lines 216-231). Replace:

```csharp
        var template = new SealedProductTemplate
        {
            Name = $"{setCode ?? "Generic"} {FormatProductType(productType)}",
            SetCode = setCode,
            ProductType = productType,
        };
```

With:

```csharp
        var template = new SealedProductTemplate
        {
            Name = SealedProductArchetypeRegistry.GenerateTemplateName(productType, setCode),
            SetCode = setCode,
            ProductType = productType,
        };
```

Note: This uses `setCode` as the name portion (since we don't have the full set name in this context), which is the same behavior as before but now goes through the archetype name pattern. The generic template lookup still works because it matches on `productType + setCode + upc==null`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test OmniCard.Tests --filter "FullyQualifiedName~SealedProductServiceTests" --no-restore`
Expected: All tests pass (existing + 4 new ones).

- [ ] **Step 6: Commit**

```bash
git add OmniCard/Services/SealedProductService.cs OmniCard.Tests/Services/SealedProductServiceTests.cs
git commit -m "feat: add CreateTemplateFromArchetype to SealedProductService"
```

---

### Task 5: Create SealedProductEntryView and ViewModel

**Files:**
- Create: `OmniCard/Views/SealedProductEditor/SealedProductEntryViewModel.cs`
- Create: `OmniCard/Views/SealedProductEditor/SealedProductEntryView.xaml`
- Create: `OmniCard/Views/SealedProductEditor/SealedProductEntryView.xaml.cs`

**Interfaces:**
- Consumes: `ISealedProductService.FindTemplateByUpc()`, `ISealedProductService.CreateTemplateFromArchetype()`, `ISealedProductService.AddInstance()`, `ISealedProductService.DeleteInstance()` from Tasks 3-4; `SealedProductArchetypeRegistry.GetTypesGroupedByTier()`, `SealedProductArchetypeRegistry.GetDisplayName()` from Task 2; `ICardGameService.GetAvailableSets()` for set name resolution
- Produces: `SealedProductEntryView` / `SealedProductEntryViewModel` — the unified scan-first entry dialog. Returns `List<SealedProductInstance>` via `Result` property containing all instances added during the session.

- [ ] **Step 1: Create SealedProductEntryViewModel**

Create `OmniCard/Views/SealedProductEditor/SealedProductEntryViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Models;
using OmniCard.Services;

namespace OmniCard.Views.SealedProductEditor;

public sealed partial class SealedProductEntryViewModel(
    ISealedProductService sealedProductService,
    IEnumerable<ICardGameService> gameServices) : ViewModel
{
    private List<SetInfo> _sets = [];

    // UPC entry
    [ObservableProperty]
    public partial string UpcEntry { get; set; } = "";

    // New product fields (shown when UPC not found or manual add)
    [ObservableProperty]
    public partial bool ShowNewProductFields { get; set; }

    [ObservableProperty]
    public partial bool IsManualAdd { get; set; }

    [ObservableProperty]
    public partial SealedProductType SelectedProductType { get; set; } = SealedProductType.PlayBoosterBox;

    [ObservableProperty]
    public partial string SetEntry { get; set; } = "";

    [ObservableProperty]
    public partial string? MatchedSetName { get; set; }

    [ObservableProperty]
    public partial string? MatchedSetCode { get; set; }

    [ObservableProperty]
    public partial string GeneratedName { get; set; } = "";

    // Price
    [ObservableProperty]
    public partial string PriceEntry { get; set; } = "";

    // Known template (set when UPC matches)
    [ObservableProperty]
    public partial SealedProductTemplate? MatchedTemplate { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "";

    // Session items
    public ObservableCollection<SessionItem> SessionItems { get; } = [];

    // All product types for the dropdown (uses ProductTypeDisplayConverter for display)
    public IReadOnlyList<SealedProductType> AllProductTypes { get; } =
        Enum.GetValues<SealedProductType>().ToList();

    // Set suggestions for autocomplete
    public ObservableCollection<SetInfo> SetSuggestions { get; } = [];

    public List<SealedProductInstance> Result { get; } = [];
    public Action<bool>? CloseDialog { get; set; }
    public Action? FocusUpcField { get; set; }
    public Action? FocusPriceField { get; set; }

    public void Load()
    {
        // Load sets from all game services (primarily MTG)
        _sets = gameServices
            .SelectMany(s => s.GetAvailableSets())
            .DistinctBy(s => s.SetCode)
            .OrderBy(s => s.SetName)
            .ToList();

        // AllProductTypes is already initialized via property initializer
    }

    partial void OnSetEntryChanged(string value)
    {
        UpdateSetSuggestions(value);
        TryMatchSet(value);
        UpdateGeneratedName();
    }

    partial void OnSelectedProductTypeChanged(SealedProductType value)
    {
        UpdateGeneratedName();
    }

    private void UpdateSetSuggestions(string text)
    {
        SetSuggestions.Clear();
        if (string.IsNullOrWhiteSpace(text) || text.Length < 2) return;

        var matches = _sets
            .Where(s => s.SetName.Contains(text, StringComparison.OrdinalIgnoreCase)
                     || s.SetCode.Contains(text, StringComparison.OrdinalIgnoreCase))
            .Take(10);

        foreach (var s in matches)
            SetSuggestions.Add(s);
    }

    private void TryMatchSet(string text)
    {
        var match = _sets.FirstOrDefault(s =>
            s.SetCode.Equals(text, StringComparison.OrdinalIgnoreCase)
            || s.SetName.Equals(text, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            MatchedSetCode = match.SetCode;
            MatchedSetName = match.SetName;
        }
        else
        {
            MatchedSetCode = null;
            MatchedSetName = null;
        }
    }

    private void UpdateGeneratedName()
    {
        GeneratedName = SealedProductArchetypeRegistry.GenerateTemplateName(
            SelectedProductType, MatchedSetName ?? (string.IsNullOrWhiteSpace(SetEntry) ? null : SetEntry));
    }

    public void SelectSet(SetInfo set)
    {
        MatchedSetCode = set.SetCode;
        MatchedSetName = set.SetName;
        SetEntry = set.SetName;
        SetSuggestions.Clear();
        UpdateGeneratedName();
    }

    [RelayCommand]
    public void LookupUpc()
    {
        if (string.IsNullOrWhiteSpace(UpcEntry)) return;

        var template = sealedProductService.FindTemplateByUpc(UpcEntry.Trim());
        if (template is not null)
        {
            MatchedTemplate = template;
            ShowNewProductFields = false;
            StatusMessage = $"Found: {template.Name}";
            FocusPriceField?.Invoke();
        }
        else
        {
            MatchedTemplate = null;
            ShowNewProductFields = true;
            IsManualAdd = false;
            StatusMessage = "UPC not found — define the product below.";
        }
    }

    [RelayCommand]
    public void ManualAdd()
    {
        MatchedTemplate = null;
        ShowNewProductFields = true;
        IsManualAdd = true;
        UpcEntry = "";
        StatusMessage = "Manual entry — pick type and set.";
    }

    [RelayCommand]
    public void AddProduct()
    {
        decimal? price = decimal.TryParse(PriceEntry, out var parsed) ? parsed : null;

        SealedProductTemplate template;
        if (MatchedTemplate is not null)
        {
            template = MatchedTemplate;
        }
        else
        {
            var upc = IsManualAdd ? null : UpcEntry.Trim();
            template = sealedProductService.CreateTemplateFromArchetype(
                SelectedProductType,
                MatchedSetCode ?? (string.IsNullOrWhiteSpace(SetEntry) ? null : SetEntry.Trim()),
                MatchedSetName,
                string.IsNullOrWhiteSpace(upc) ? null : upc);
        }

        var instance = sealedProductService.AddInstance(template.Id, price);
        Result.Add(instance);

        SessionItems.Insert(0, new SessionItem(
            instance.Id,
            template.Name,
            price,
            SealedProductArchetypeRegistry.GetDisplayName(template.ProductType)));

        // Reset for next entry
        StatusMessage = $"Added: {template.Name}";
        UpcEntry = "";
        PriceEntry = "";
        SetEntry = "";
        MatchedTemplate = null;
        ShowNewProductFields = false;
        IsManualAdd = false;
        MatchedSetCode = null;
        MatchedSetName = null;
        GeneratedName = "";

        FocusUpcField?.Invoke();
    }

    [RelayCommand]
    public void RemoveSessionItem(SessionItem item)
    {
        sealedProductService.DeleteInstance(item.InstanceId);
        Result.RemoveAll(i => i.Id == item.InstanceId);
        SessionItems.Remove(item);
        StatusMessage = $"Removed: {item.Name}";
    }

    [RelayCommand]
    public void Done() => CloseDialog?.Invoke(true);
}

public record SessionItem(int InstanceId, string Name, decimal? Price, string TypeDisplay);

public class ProductTypeDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is SealedProductType type ? SealedProductArchetypeRegistry.GetDisplayName(type) : value?.ToString() ?? "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

- [ ] **Step 2: Create SealedProductEntryView XAML**

Create `OmniCard/Views/SealedProductEditor/SealedProductEntryView.xaml`:

```xml
<Window x:Class="OmniCard.Views.SealedProductEditor.SealedProductEntryView"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:OmniCard.Views.SealedProductEditor"
        xmlns:models="clr-namespace:OmniCard.Models"
        ShowInTaskbar="False"
        WindowStartupLocation="CenterOwner"
        Title="Add Sealed Products" Height="600" Width="550"
        Background="{DynamicResource MaterialDesign.Brush.Background}"
        TextElement.Foreground="{DynamicResource MaterialDesign.Brush.Foreground}"
        TextElement.FontWeight="Regular"
        TextElement.FontSize="13"
        FontFamily="{StaticResource AppFont}">

    <Window.Resources>
        <BooleanToVisibilityConverter x:Key="BoolToVis"/>
        <local:ProductTypeDisplayConverter x:Key="ProductTypeDisplayConverter"/>
    </Window.Resources>

    <Grid Margin="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="1*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- UPC Scan Section -->
        <Border Background="{DynamicResource MaterialDesign.Brush.Card.Background}"
                CornerRadius="4" Padding="12" Margin="0,0,0,8">
            <StackPanel>
                <TextBlock Text="Scan or type UPC:" FontWeight="SemiBold" Margin="0,0,0,4"/>
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <TextBox x:Name="UpcField"
                             Text="{Binding ViewModel.UpcEntry, UpdateSourceTrigger=PropertyChanged}"
                             VerticalContentAlignment="Center" Margin="0,0,4,0">
                        <TextBox.InputBindings>
                            <KeyBinding Key="Return" Command="{Binding ViewModel.LookupUpcCommand}"/>
                        </TextBox.InputBindings>
                    </TextBox>
                    <Button Grid.Column="1" Content="Lookup" Padding="12,4" Margin="0,0,4,0"
                            Command="{Binding ViewModel.LookupUpcCommand}"/>
                    <Button Grid.Column="2" Content="Manual Add" Padding="12,4"
                            Command="{Binding ViewModel.ManualAddCommand}"
                            Style="{StaticResource MaterialDesignFlatButton}"/>
                </Grid>
            </StackPanel>
        </Border>

        <!-- Status -->
        <TextBlock Grid.Row="1" Text="{Binding ViewModel.StatusMessage}"
                   FontStyle="Italic" Margin="0,0,0,8"
                   Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}"/>

        <!-- New Product Fields (shown when UPC not found or manual add) -->
        <Border Grid.Row="2"
                Visibility="{Binding ViewModel.ShowNewProductFields, Converter={StaticResource BoolToVis}}"
                Background="{DynamicResource MaterialDesign.Brush.Card.Background}"
                CornerRadius="4" Padding="12" Margin="0,0,0,8">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                </Grid.RowDefinitions>

                <TextBlock Text="Type:" FontWeight="SemiBold" VerticalAlignment="Center" Margin="0,4,8,4"/>
                <ComboBox Grid.Column="1" Margin="0,2"
                          ItemsSource="{Binding ViewModel.AllProductTypes}"
                          SelectedItem="{Binding ViewModel.SelectedProductType}">
                    <ComboBox.ItemTemplate>
                        <DataTemplate>
                            <TextBlock Text="{Binding Converter={StaticResource ProductTypeDisplayConverter}}"/>
                        </DataTemplate>
                    </ComboBox.ItemTemplate>
                </ComboBox>

                <TextBlock Grid.Row="1" Text="Set:" FontWeight="SemiBold" VerticalAlignment="Center" Margin="0,4,8,4"/>
                <TextBox Grid.Row="1" Grid.Column="1" Margin="0,2"
                         Text="{Binding ViewModel.SetEntry, UpdateSourceTrigger=PropertyChanged}"
                         VerticalContentAlignment="Center"/>

                <TextBlock Grid.Row="2" Text="Name:" FontWeight="SemiBold" VerticalAlignment="Center" Margin="0,4,8,4"/>
                <TextBlock Grid.Row="2" Grid.Column="1" Margin="0,6"
                           Text="{Binding ViewModel.GeneratedName}"
                           FontStyle="Italic"
                           Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}"/>
            </Grid>
        </Border>

        <!-- Price + Add -->
        <Border Grid.Row="3" Background="{DynamicResource MaterialDesign.Brush.Card.Background}"
                CornerRadius="4" Padding="12" Margin="0,0,0,8">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="100"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <TextBlock Text="Price $:" FontWeight="SemiBold" VerticalAlignment="Center" Margin="0,0,8,0"/>
                <TextBox Grid.Column="1" x:Name="PriceField"
                         Text="{Binding ViewModel.PriceEntry, UpdateSourceTrigger=PropertyChanged}"
                         VerticalContentAlignment="Center">
                    <TextBox.InputBindings>
                        <KeyBinding Key="Return" Command="{Binding ViewModel.AddProductCommand}"/>
                    </TextBox.InputBindings>
                </TextBox>
                <Button Grid.Column="3" Content="Add" Padding="16,4" FontWeight="SemiBold"
                        Command="{Binding ViewModel.AddProductCommand}"/>
            </Grid>
        </Border>

        <!-- Session List -->
        <Grid Grid.Row="4">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="1*"/>
            </Grid.RowDefinitions>
            <TextBlock Text="Added this session:" FontWeight="SemiBold" Margin="0,0,0,4"/>
            <ListBox Grid.Row="1" ItemsSource="{Binding ViewModel.SessionItems}"
                     BorderThickness="0">
                <ListBox.ItemTemplate>
                    <DataTemplate DataType="{x:Type local:SessionItem}">
                        <Grid Margin="0,2">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="80"/>
                                <ColumnDefinition Width="Auto"/>
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="{Binding Name}" VerticalAlignment="Center"/>
                            <TextBlock Grid.Column="1" VerticalAlignment="Center"
                                       Text="{Binding Price, StringFormat=${0:F2}, TargetNullValue='—'}"
                                       HorizontalAlignment="Right" Margin="0,0,8,0"/>
                            <Button Grid.Column="2" Content="X" Padding="6,2"
                                    Command="{Binding DataContext.ViewModel.RemoveSessionItemCommand,
                                        RelativeSource={RelativeSource AncestorType=Window}}"
                                    CommandParameter="{Binding}"/>
                        </Grid>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>

            <!-- Empty state -->
            <TextBlock Grid.Row="1" Text="No items added yet"
                       HorizontalAlignment="Center" VerticalAlignment="Center"
                       FontStyle="Italic"
                       Foreground="{DynamicResource MaterialDesign.Brush.Foreground.Light}"
                       IsHitTestVisible="False">
                <TextBlock.Style>
                    <Style TargetType="TextBlock">
                        <Setter Property="Visibility" Value="Collapsed"/>
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding ViewModel.SessionItems.Count}" Value="0">
                                <Setter Property="Visibility" Value="Visible"/>
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </TextBlock.Style>
            </TextBlock>
        </Grid>

        <!-- Done button -->
        <StackPanel Grid.Row="5" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,8,0,0">
            <Button Content="Done" Command="{Binding ViewModel.DoneCommand}"
                    IsDefault="True" Padding="16,6" FontWeight="SemiBold"/>
        </StackPanel>
    </Grid>
</Window>
```

- [ ] **Step 3: Create SealedProductEntryView code-behind**

Create `OmniCard/Views/SealedProductEditor/SealedProductEntryView.xaml.cs`:

```csharp
namespace OmniCard.Views.SealedProductEditor;

public partial class SealedProductEntryView : IView<SealedProductEntryViewModel>
{
    public SealedProductEntryView(SealedProductEntryViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        ViewModel.CloseDialog = result =>
        {
            DialogResult = result;
            Close();
        };
        ViewModel.FocusUpcField = () => UpcField.Focus();
        ViewModel.FocusPriceField = () => PriceField.Focus();
        DataContext = this;
    }

    public SealedProductEntryViewModel ViewModel { get; }

    IViewModel IView.ViewModel => ViewModel;
}
```

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build OmniCard/OmniCard.csproj`
Expected: Clean build. The new view/viewmodel compile but aren't wired into DI yet (that's Task 7).

- [ ] **Step 5: Commit**

```bash
git add OmniCard/Views/SealedProductEditor/SealedProductEntryViewModel.cs OmniCard/Views/SealedProductEditor/SealedProductEntryView.xaml OmniCard/Views/SealedProductEditor/SealedProductEntryView.xaml.cs
git commit -m "feat: add SealedProductEntryView with scan-first bulk entry workflow"
```

---

### Task 6: Update SealedProductTemplateEditorView for Expanded Types

**Files:**
- Modify: `OmniCard/Views/SealedProductEditor/SealedProductTemplateEditorView.xaml` (update ComboBox items)
- Modify: `OmniCard/Views/SealedProductEditor/SealedProductTemplateEditorViewModel.cs` (archetype auto-fill on type change)

**Interfaces:**
- Consumes: `SealedProductArchetypeRegistry.GetArchetype()`, `SealedProductArchetypeRegistry.GetDisplayName()` from Task 2
- Produces: Updated template editor that offers archetype auto-fill when changing product type

- [ ] **Step 1: Update the ProductType ComboBox in the template editor XAML**

In `OmniCard/Views/SealedProductEditor/SealedProductTemplateEditorView.xaml`, replace the ProductType ComboBox (lines 38-44) that has hardcoded enum values. Replace the entire `<ComboBox Grid.Row="1" Grid.Column="1" ...>` block with an ItemsSource-bound version:

Replace:
```xml
                <ComboBox Grid.Row="1" Grid.Column="1"
                          SelectedItem="{Binding ViewModel.ProductType}" Margin="0,2,8,2">
                    <models:SealedProductType>Case</models:SealedProductType>
                    <models:SealedProductType>BoosterBox</models:SealedProductType>
                    <models:SealedProductType>BundleBox</models:SealedProductType>
                    <models:SealedProductType>CollectorBoosterPack</models:SealedProductType>
                    <models:SealedProductType>BoosterPack</models:SealedProductType>
                    <models:SealedProductType>PromoPack</models:SealedProductType>
                    <models:SealedProductType>FixedPack</models:SealedProductType>
                    <models:SealedProductType>Card</models:SealedProductType>
                </ComboBox>
```

With:
```xml
                <ComboBox Grid.Row="1" Grid.Column="1"
                          ItemsSource="{Binding ViewModel.AllProductTypes}"
                          SelectedItem="{Binding ViewModel.ProductType}" Margin="0,2,8,2">
                    <ComboBox.ItemTemplate>
                        <DataTemplate>
                            <TextBlock Text="{Binding Converter={StaticResource ProductTypeDisplayConverter}}"/>
                        </DataTemplate>
                    </ComboBox.ItemTemplate>
                </ComboBox>
```

Add a converter to the Window.Resources section (add a `<Window.Resources>` block if not present):
```xml
    <Window.Resources>
        <local:ProductTypeDisplayConverter x:Key="ProductTypeDisplayConverter"/>
    </Window.Resources>
```

- [ ] **Step 2: Also update the ChildProductType ComboBox in the contents ItemTemplate**

In the same XAML file, replace the ChildProductType ComboBox in the contents DataTemplate (lines 60-65):

Replace:
```xml
                                <ComboBox SelectedItem="{Binding ChildProductType}" Width="160" Margin="0,0,8,0">
                                    <models:SealedProductType>Case</models:SealedProductType>
                                    <models:SealedProductType>BoosterBox</models:SealedProductType>
                                    <models:SealedProductType>BundleBox</models:SealedProductType>
                                    <models:SealedProductType>CollectorBoosterPack</models:SealedProductType>
                                    <models:SealedProductType>BoosterPack</models:SealedProductType>
                                    <models:SealedProductType>PromoPack</models:SealedProductType>
                                    <models:SealedProductType>FixedPack</models:SealedProductType>
                                    <models:SealedProductType>Card</models:SealedProductType>
                                </ComboBox>
```

With:
```xml
                                <ComboBox SelectedItem="{Binding ChildProductType}" Width="200" Margin="0,0,8,0"
                                          ItemsSource="{Binding DataContext.ViewModel.AllProductTypes,
                                              RelativeSource={RelativeSource AncestorType=Window}}">
                                    <ComboBox.ItemTemplate>
                                        <DataTemplate>
                                            <TextBlock Text="{Binding Converter={StaticResource ProductTypeDisplayConverter}}"/>
                                        </DataTemplate>
                                    </ComboBox.ItemTemplate>
                                </ComboBox>
```

- [ ] **Step 3: Add the converter class and AllProductTypes property to the ViewModel**

In `OmniCard/Views/SealedProductEditor/SealedProductTemplateEditorViewModel.cs`, add near the top (after the usings):

```csharp
using System.Globalization;
using System.Windows.Data;
```

Note: The `ProductTypeDisplayConverter` class was already created in Task 5 inside `SealedProductEntryViewModel.cs`. It's in the same namespace (`OmniCard.Views.SealedProductEditor`), so the XAML can reference it directly.

In the `SealedProductTemplateEditorViewModel` class, add a property:

```csharp
    public IReadOnlyList<SealedProductType> AllProductTypes { get; } =
        Enum.GetValues<SealedProductType>().ToList();
```

- [ ] **Step 4: Add archetype auto-fill behavior on type change**

In the `SealedProductTemplateEditorViewModel` class, add a partial method that responds to ProductType changes:

```csharp
    partial void OnProductTypeChanged(SealedProductType value)
    {
        if (ContentLines.Count == 0)
        {
            // Auto-fill contents from archetype when contents are empty
            var archetype = SealedProductArchetypeRegistry.GetArchetype(value);
            foreach (var content in archetype.DefaultContents)
            {
                ContentLines.Add(new ContentLineItem
                {
                    Quantity = content.Quantity,
                    ChildProductType = content.ChildType,
                });
            }
        }
    }
```

Also update the `AddContentLine` method's default to use `PlayBoosterPack` instead of `BoosterPack` (more common for modern sets):

```csharp
    [RelayCommand]
    public void AddContentLine()
    {
        ContentLines.Add(new ContentLineItem { Quantity = 1, ChildProductType = SealedProductType.PlayBoosterPack });
    }
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build OmniCard/OmniCard.csproj`
Expected: Clean build.

- [ ] **Step 6: Commit**

```bash
git add OmniCard/Views/SealedProductEditor/SealedProductTemplateEditorView.xaml OmniCard/Views/SealedProductEditor/SealedProductTemplateEditorViewModel.cs
git commit -m "feat: update template editor with expanded product types and archetype auto-fill"
```

---

### Task 7: Update DialogService, DI Wiring, and SealedProductViewModel

**Files:**
- Modify: `OmniCard/Services/DialogService.cs` (replace `AddSealedProduct` with `OpenSealedProductEntry`)
- Modify: `OmniCard/App.xaml.cs` (register new view/VM, remove old)
- Modify: `OmniCard/Views/Root/SealedProductViewModel.cs` (use new entry dialog)
- Modify: `OmniCard/Views/Root/SealedProductListView.xaml` (toolbar changes, type display)

**Interfaces:**
- Consumes: `SealedProductEntryView` / `SealedProductEntryViewModel` from Task 5; `SealedProductArchetypeRegistry.GetDisplayName()` from Task 2
- Produces: Fully wired entry flow — "Add Products..." button opens the new dialog, UPC scanning moved into dialog, type column shows human-readable names

- [ ] **Step 1: Update IDialogService and DialogService**

In `OmniCard/Services/DialogService.cs`, replace the `AddSealedProduct` interface method (line 33):

```csharp
    List<SealedProductInstance>? OpenSealedProductEntry();
```

Replace the `AddSealedProduct` implementation (lines 148-157):

```csharp
    public List<SealedProductInstance>? OpenSealedProductEntry()
    {
        var wnd = Services.GetRequiredService<SealedProductEntryView>();
        wnd.Owner = Application.Current.MainWindow;
        wnd.ViewModel.Load();
        var result = wnd.ShowDialog();
        return result == true ? wnd.ViewModel.Result : null;
    }
```

Add the necessary using at the top if not present:
```csharp
using OmniCard.Views.SealedProductEditor;
```

- [ ] **Step 2: Update DI registrations in App.xaml.cs**

In `OmniCard/App.xaml.cs`, in the transient services section (around lines 135-140), replace:

```csharp
            services.AddTransient<AddSealedProductView>();
            services.AddTransient<AddSealedProductViewModel>();
```

With:

```csharp
            services.AddTransient<SealedProductEntryView>();
            services.AddTransient<SealedProductEntryViewModel>();
```

- [ ] **Step 3: Update SealedProductViewModel**

In `OmniCard/Views/Root/SealedProductViewModel.cs`:

Replace the `AddByUpc` method (lines 42-76) with:

```csharp
    [RelayCommand]
    public void AddProducts()
    {
        var added = _dialogService.OpenSealedProductEntry();
        if (added is not null && added.Count > 0)
        {
            ReportMessage?.Invoke($"Added {added.Count} sealed product(s).");
            LoadInstances();
        }
    }
```

Replace the `AddByTemplate` method (lines 77-86) — it is no longer needed. Remove it entirely.

Remove the `UpcEntry` property (lines 26-27) — UPC entry is now inside the dialog.

Update the `CrackInstance` method: the `SealedProductType.Card` check on line 102 is still valid since `Card` is still in the enum.

- [ ] **Step 4: Update SealedProductListView toolbar**

In `OmniCard/Views/Root/SealedProductListView.xaml`, replace the entire toolbar section (lines 13-32):

```xml
        <!-- Entry toolbar -->
        <Border Padding="8" Background="{DynamicResource MaterialDesign.Brush.Card.Background}">
            <StackPanel Orientation="Horizontal">
                <Button Content="Add Products..." Command="{Binding AddProductsCommand}"
                        Padding="12,4" FontWeight="SemiBold" Margin="0,0,12,0"/>
                <Separator/>
                <Button Content="Manage Templates..." Command="{Binding ManageTemplatesCommand}"
                        Padding="8,4" Margin="8,0,0,0"
                        Style="{StaticResource MaterialDesignFlatButton}"/>
            </StackPanel>
        </Border>
```

Update the Type column in the DataGrid (line 48) to use a converter for display names. Add a Window.Resources or UserControl.Resources section with the converter, and update the column:

Add at the top of the UserControl (before the `<Grid>`):
```xml
    <UserControl.Resources>
        <local:ProductTypeDisplayConverter x:Key="ProductTypeDisplayConverter"/>
    </UserControl.Resources>
```

You'll need to add the `xmlns:local` namespace if it references the SealedProductEditor namespace. Alternatively, since `ProductTypeDisplayConverter` is in the `SealedProductEditor` namespace, add:
```xml
xmlns:editor="clr-namespace:OmniCard.Views.SealedProductEditor"
```
And use `<editor:ProductTypeDisplayConverter>` as the key.

Then change the Type column binding (line 48):
```xml
                <DataGridTextColumn Header="Type"
                                    Binding="{Binding Template.ProductType, Converter={StaticResource ProductTypeDisplayConverter}}"
                                    Width="150" IsReadOnly="True"/>
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build OmniCard/OmniCard.csproj`
Expected: Clean build. May have warnings if `AddByUpcCommand` is still referenced somewhere — check and remove any remaining references.

- [ ] **Step 6: Run all tests**

Run: `dotnet test OmniCard.Tests`
Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add OmniCard/Services/DialogService.cs OmniCard/App.xaml.cs OmniCard/Views/Root/SealedProductViewModel.cs OmniCard/Views/Root/SealedProductListView.xaml
git commit -m "feat: wire up scan-first entry dialog and update toolbar"
```

---

### Task 8: Remove Old AddSealedProductView

**Files:**
- Delete: `OmniCard/Views/SealedProductEditor/AddSealedProductView.xaml`
- Delete: `OmniCard/Views/SealedProductEditor/AddSealedProductView.xaml.cs`
- Delete: `OmniCard/Views/SealedProductEditor/AddSealedProductViewModel.cs`

**Interfaces:**
- Consumes: Nothing — this is cleanup after Task 7 replaced all usages
- Produces: Clean codebase with no dead code

- [ ] **Step 1: Verify no remaining references to the old types**

Search the codebase for `AddSealedProductView` and `AddSealedProductViewModel`. After Task 7, there should be zero references outside the files being deleted.

Run: `grep -r "AddSealedProduct" OmniCard/ --include="*.cs" --include="*.xaml" -l`
Expected: Only the three files to be deleted should match.

- [ ] **Step 2: Delete the files**

```bash
rm OmniCard/Views/SealedProductEditor/AddSealedProductView.xaml
rm OmniCard/Views/SealedProductEditor/AddSealedProductView.xaml.cs
rm OmniCard/Views/SealedProductEditor/AddSealedProductViewModel.cs
```

- [ ] **Step 3: Build and run tests**

Run: `dotnet build && dotnet test OmniCard.Tests`
Expected: Clean build, all tests pass.

- [ ] **Step 4: Commit**

```bash
git add -u OmniCard/Views/SealedProductEditor/
git commit -m "chore: remove replaced AddSealedProductView"
```

---

### Task 9: Update and Add Tests

**Files:**
- Modify: `OmniCard.Tests/Services/SealedProductServiceTests.cs` (add archetype-based tests, update existing)
- Modify: `OmniCard.Tests/Data/SealedProductDbContextTests.cs` (verify new enum values persist correctly)

**Interfaces:**
- Consumes: All service methods from Tasks 3-4
- Produces: Comprehensive test coverage for the reworked sealed product feature

- [ ] **Step 1: Add DB round-trip test for new enum values**

Add to `OmniCard.Tests/Data/SealedProductDbContextTests.cs`:

```csharp
    [Theory]
    [InlineData(SealedProductType.PlayBoosterBox)]
    [InlineData(SealedProductType.CollectorBoosterBox)]
    [InlineData(SealedProductType.Bundle)]
    [InlineData(SealedProductType.CommanderDeck)]
    [InlineData(SealedProductType.SecretLair)]
    [InlineData(SealedProductType.PrereleaseKit)]
    public void NewProductTypes_RoundTripThroughDb(SealedProductType type)
    {
        using var ctx = new SealedProductDbContext(_options);

        var template = new SealedProductTemplate
        {
            Name = $"Test {type}",
            ProductType = type,
        };
        ctx.Templates.Add(template);
        ctx.SaveChanges();

        var loaded = ctx.Templates.First(t => t.Name == $"Test {type}");
        Assert.Equal(type, loaded.ProductType);
    }
```

- [ ] **Step 2: Add test verifying archetype-created template cracks correctly**

Add to `OmniCard.Tests/Services/SealedProductServiceTests.cs`:

```csharp
    [Fact]
    public void CreateTemplateFromArchetype_ThenCrack_ProducesCorrectChildren()
    {
        var service = CreateService();
        var template = service.CreateTemplateFromArchetype(
            SealedProductType.PlayBoosterBox, "mh3", "Modern Horizons 3", null);

        var instance = service.AddInstance(template.Id, 180m);
        var children = service.CrackInstance(instance.Id);

        Assert.Equal(36, children.Count);
        Assert.All(children, c => Assert.Equal(5m, c.PurchasePrice)); // 180 / 36 = 5
    }

    [Fact]
    public void CreateTemplateFromArchetype_PrereleaseKit_CracksIntoMixedTypes()
    {
        var service = CreateService();
        var template = service.CreateTemplateFromArchetype(
            SealedProductType.PrereleaseKit, "mkm", "Murders at Karlov Manor", null);

        var instance = service.AddInstance(template.Id, 35m);
        var children = service.CrackInstance(instance.Id);

        Assert.Equal(7, children.Count); // 6 play boosters + 1 promo
        Assert.All(children, c => Assert.Equal(5m, c.PurchasePrice)); // 35 / 7 = 5
    }
```

- [ ] **Step 3: Run all tests**

Run: `dotnet test OmniCard.Tests`
Expected: All tests pass — existing + new archetype registry tests + new service tests + new DB tests.

- [ ] **Step 4: Commit**

```bash
git add OmniCard.Tests/
git commit -m "test: add comprehensive tests for expanded sealed product types and archetype flow"
```
