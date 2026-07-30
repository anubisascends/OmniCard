# Sealed Product Rework — Design Spec

**Date:** 2026-07-06
**Status:** Draft
**Goal:** Rework the sealed product feature to support the full range of MTG sealed product types across all eras, with a scan-first bulk entry workflow driven by archetype-based auto-fill.

---

## 1. Problem Statement

The current sealed product feature has a limited product type enum (8 types), requires manual template creation for every product, and uses a two-dialog entry flow (UPC scan vs. template pick) that slows down bulk cataloging sessions. Users cataloging backlogs of diverse sealed product across many sets and eras need a faster, smarter workflow.

## 2. Approach: Product Archetypes

Each `SealedProductType` enum value maps to a static **archetype definition** in code that knows:
- A display name pattern (e.g., `"{SetName} Play Booster Box"`)
- Default contents (e.g., 36× PlayBoosterPack)
- A tier classification for UI grouping and crack logic

Archetypes are consulted only at **template creation time** — once a template is saved to the DB, it's independent of the archetype. Templates remain user-editable for products that deviate from defaults.

## 3. Expanded SealedProductType Enum

Replaces the current 8 types with ~28 covering all MTG eras.

### Cases
- `Case`

### Boxes
- `PlayBoosterBox`
- `DraftBoosterBox`
- `SetBoosterBox`
- `CollectorBoosterBox`
- `ThemeBoosterBox`
- `BoosterBox` (generic / old-frame, replaces the existing value)

### Packs
- `PlayBoosterPack`
- `DraftBoosterPack`
- `SetBoosterPack`
- `CollectorBoosterPack`
- `ThemeBoosterPack`
- `BoosterPack` (generic / old-frame, keeps existing value)
- `PromoPack` (keeps existing value)

### Bundles & Kits
- `Bundle` (replaces the old `BundleBox` value)
- `GiftBundle`
- `FatPack`
- `PrereleaseKit`
- `StarterKit`

### Decks & Fixed Products
- `CommanderDeck`
- `PlaneswalkerDeck`
- `IntroPack`
- `ThemeDeck`
- `IntroDeck`
- `WelcomeDeck`
- `FixedPack` (keeps existing value)

### Special Products
- `SecretLair`
- `FromTheVault`
- `BlisterPack`

### Terminal
- `Card` (keeps existing value)

### Legacy Mapping
- `BundleBox` → `Bundle` (DB migration renames stored string values)

## 4. Archetype Definitions

### ArchetypeTier Enum
`Case`, `Box`, `Pack`, `Card`, `Deck`, `Kit`, `Special`

Used for UI grouping (dropdown sections) and crack-flow hinting. Not stored in the database.

### Archetype Registry (static, code-only)

| Type | Name Pattern | Default Contents | Tier |
|------|-------------|-----------------|------|
| Case | {Set} {ChildType} Case | 6× (inferred box type) | Case |
| PlayBoosterBox | {Set} Play Booster Box | 36× PlayBoosterPack | Box |
| DraftBoosterBox | {Set} Draft Booster Box | 36× DraftBoosterPack | Box |
| SetBoosterBox | {Set} Set Booster Box | 30× SetBoosterPack | Box |
| CollectorBoosterBox | {Set} Collector Booster Box | 12× CollectorBoosterPack | Box |
| ThemeBoosterBox | {Set} Theme Booster Box | 12× ThemeBoosterPack | Box |
| BoosterBox | {Set} Booster Box | 36× BoosterPack | Box |
| PlayBoosterPack | {Set} Play Booster Pack | *(leaf — no contents)* | Pack |
| DraftBoosterPack | {Set} Draft Booster Pack | *(leaf)* | Pack |
| SetBoosterPack | {Set} Set Booster Pack | *(leaf)* | Pack |
| CollectorBoosterPack | {Set} Collector Booster Pack | *(leaf)* | Pack |
| ThemeBoosterPack | {Set} Theme Booster Pack | *(leaf)* | Pack |
| BoosterPack | {Set} Booster Pack | *(leaf)* | Pack |
| PromoPack | {Set} Promo Pack | *(leaf)* | Pack |
| Bundle | {Set} Bundle | 8× PlayBoosterPack | Kit |
| GiftBundle | {Set} Gift Bundle | 10× PlayBoosterPack | Kit |
| FatPack | {Set} Fat Pack | 9× BoosterPack | Kit |
| PrereleaseKit | {Set} Prerelease Kit | 6× PlayBoosterPack + 1× PromoPack | Kit |
| StarterKit | {Set} Starter Kit | 2× FixedPack | Kit |
| CommanderDeck | {Set} Commander Deck | 1× FixedPack | Deck |
| PlaneswalkerDeck | {Set} Planeswalker Deck | 1× FixedPack + 1× BoosterPack | Deck |
| IntroPack | {Set} Intro Pack | 1× FixedPack + 2× BoosterPack | Deck |
| ThemeDeck | {Set} Theme Deck | 1× FixedPack | Deck |
| IntroDeck | {Set} Intro Deck | 1× FixedPack | Deck |
| WelcomeDeck | {Set} Welcome Deck | 1× FixedPack | Deck |
| SecretLair | Secret Lair: {Set} | 1× FixedPack | Special |
| FromTheVault | From the Vault: {Set} | 1× FixedPack | Special |
| BlisterPack | {Set} Blister Pack | 3× BoosterPack | Pack |
| FixedPack | {Set} Fixed Pack | *(leaf)* | Pack |
| Card | *(N/A)* | *(terminal)* | Card |

### Archetype Behavior
- Consulted at template creation time only
- `{Set}` in name patterns is substituted with the **full set name** (e.g., "Modern Horizons 3"), resolved from the set code via the app's existing set data
- Auto-populates template contents from default contents list
- For `Case`, the user specifies which box type the case contains; archetype infers 6× that box type
- Templates are always editable after creation — archetypes are suggestions, not constraints

## 5. Data Model Changes

### Schema Changes: None
The existing template/instance model is preserved:
- **SealedProductTemplate**: `Id`, `Name`, `SetCode`, `Upc`, `ProductType`, `Contents`
- **SealedProductInstance**: `Id`, `TemplateId`, `PurchasePrice`, `DateAdded`
- **SealedProductContents**: `Id`, `TemplateId`, `Quantity`, `ChildProductType`, `ChildTemplateId`

### DB Migration
- Rename stored `BundleBox` enum string values to `Bundle` in the Templates table's `ProductType` column
- Rename stored `BundleBox` enum string values to `Bundle` in the TemplateContents table's `ChildProductType` column

### New Code-Only Types
- `SealedProductArchetype` record/class — holds name pattern, default contents list, and tier
- `ArchetypeTier` enum — `Case`, `Box`, `Pack`, `Card`, `Deck`, `Kit`, `Special`
- `SealedProductArchetypeRegistry` static class — dictionary mapping `SealedProductType` → `SealedProductArchetype`

## 6. Scan-First Entry Workflow

### New Dialog: SealedProductEntryView

Replaces the existing `AddSealedProductView` with a unified, stay-open entry dialog.

### Flow A: Known UPC
1. User scans/types UPC → template found instantly
2. Purchase price field auto-focuses
3. User enters price, hits Enter → instance created
4. Focus returns to UPC field for next scan

### Flow B: Unknown UPC
1. User scans/types UPC → no match → inline "New Product" section appears
2. User picks **Product Type** from a grouped dropdown (grouped by `ArchetypeTier`)
3. User picks/types **Set** (autocomplete from sets known to the app)
4. Archetype auto-fills template name and default contents
5. User enters purchase price, hits Enter
6. Template saved (with UPC), instance created, focus returns to UPC field
7. Future scans of this UPC are instant (Flow A)

### Flow C: No UPC (manual entry)
1. User clicks "Manual Add" button
2. Same as Flow B steps 2-6, but no UPC is attached to the template

### UX Details
- Dialog **stays open** for continuous entry — no open/close per item
- Running "session list" at the bottom shows items added this session
- Each session list row has a quick delete (X) button for mis-scans
- Purchase price field accepts bare numbers (no `$` required)
- UPC field re-focuses automatically after each successful add
- Product type dropdown is grouped by tier with section headers (Boxes, Packs, Kits & Bundles, Decks, Special)

## 7. UI Changes

### New
- **SealedProductEntryView / SealedProductEntryViewModel** — the unified scan-first entry dialog described in Section 6

### Replaced
- **AddSealedProductView / AddSealedProductViewModel** — removed, replaced entirely by SealedProductEntryView

### Modified: SealedProductListView (main tab)
- Toolbar: remove UPC text field and "Add" button; replace with single "Add Products..." button that opens the entry dialog
- Type column displays human-readable names (e.g., "Play Booster Box" not "PlayBoosterBox")
- DataGrid otherwise unchanged (name, type, set, UPC, editable purchase price, date added)
- Context menu unchanged (crack, edit template, delete)

### Modified: SealedProductTemplateEditorView
- Product Type dropdown updated with full ~28 type list, grouped by tier
- When changing type on a template with empty contents, auto-fill contents from archetype
- When changing type on a template with existing contents, prompt: "Replace contents with defaults for {type}?"

### Modified: CrackProductView
- No major changes — supports expanded types automatically
- Tier metadata available for future UI enhancements (not required for v1)

## 8. Service Layer Changes

### SealedProductService
- `CreateTemplateFromArchetype(SealedProductType type, string setCode, string? upc)` — new method that uses the archetype registry to auto-generate a fully populated template
- `GetOrCreateGenericTemplate()` — updated to use archetype name patterns instead of the current simple naming
- Existing CRUD and crack methods unchanged (they operate on templates/instances which haven't changed structurally)

### New: SealedProductArchetypeRegistry
- Static class with a `Dictionary<SealedProductType, SealedProductArchetype>`
- Methods: `GetArchetype(SealedProductType)`, `GetDisplayName(SealedProductType)`, `GetContentDefaults(SealedProductType)`, `GetTier(SealedProductType)`
- Consumed by the entry dialog ViewModel and the service layer

### DialogService
- `AddSealedProduct()` replaced by `OpenSealedProductEntry()` — opens the new stay-open entry dialog, returns a `List<SealedProductInstance>` of all items added during the session

## 9. Testing

### New Tests
- Archetype registry: every `SealedProductType` has a valid archetype, name patterns produce expected strings, content defaults are valid types
- `CreateTemplateFromArchetype`: produces correct template with name, contents, UPC, and set code
- Entry dialog ViewModel: UPC lookup, unknown UPC flow, manual add flow, session list management

### Updated Tests
- Existing `SealedProductServiceTests` and `SealedProductDbContextTests` updated for the expanded enum and `BundleBox` → `Bundle` migration
- Template editor tests updated for archetype auto-fill behavior

## 10. Out of Scope
- Pre-built UPC database — the UPC library is self-built by the user over time
- Set name autocomplete data source — uses whatever set data the app already has
- Tier icons or visual enhancements in the crack dialog — future improvement
- Batch price splitting across multiple items — prices are entered per item
