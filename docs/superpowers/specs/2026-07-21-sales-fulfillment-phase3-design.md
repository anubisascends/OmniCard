# Sales & Fulfillment — Phase 3 (Settings Page + Receipts) — Design

**Date:** 2026-07-21
**Status:** Approved (pending spec review)
**Author:** Andrew Riebe (with Claude)
**Parent spec:** [2026-07-20-sales-fulfillment-design.md](2026-07-20-sales-fulfillment-design.md) (§8.3)

## 1. Problem & Goals

Phases 1–2 (merged) delivered listings/pick-list, customers, orders, ship →
inventory removal + Sell movements, and the net-profit P&L. Phase 3 delivers the
**receipt / packing slip** and, as its home, a **new app-level Settings page**
that also absorbs the app's currently-scattered settings.

Concretely:

- Print a **receipt / packing slip** for an order on an 80 mm thermal printer
  (width configurable), and **export the same receipt to PDF**.
- Configure a **company profile** (name, address, logo, contact) and
  **receipt settings** (width, margins, font, show-prices, footer, default
  printer).
- Introduce a proper **Settings page** and migrate today's scattered settings
  into it: display preferences, data location, and the For-Sale location.

### Success criteria

- From a selected order, **Print Receipt** sends a correctly-formatted receipt to
  the chosen printer, and **Export PDF** writes a PDF of the same content sized to
  the configured width.
- Company profile + receipt settings persist in `sales-settings.json` and survive
  a data-directory migration (logo included).
- A single **Settings** tab exposes Display, Data Location, and Sales & Receipts
  sections; the duplicate View-menu controls are gone; the For-Sale picker lives
  in Settings and the pick/ship flows still work unchanged.
- Build + tests stay green at each of the two build checkpoints.

## 2. Non-Goals (Phase 3)

- **No P&L work** — the net-profit extension already shipped in phase 2.
- **No in-app graphical print preview** — the exported PDF serves as the preview.
- **No settings-persistence rewrite** — existing storage (`DisplaySettings` via
  `IOptions`, `sales-settings.json`, data-location service) is reused as-is; this
  phase only *surfaces* those settings in a new UI.
- TCGPlayer CSV import remains phase 4 (parent spec §8.4).

## 3. Architecture Decisions

### D1 — Settings as a top-level tab
Add a `Settings` `TabItem` to `RootView`'s `MainTabControl`, consistent with how
the Sales tab was introduced. Inside it, a standard settings layout: a left-hand
section selector (**Display · Data Location · Sales & Receipts**) that switches
the content pane on the right. Each section is its own `UserControl` + small VM.

*Rejected:* a modal Settings window opened from a menu — the codebase's
established shell pattern is tabs, and a tab keeps settings always reachable.

### D2 — Bind to existing state, don't duplicate it
The Settings page is a **new view over existing persistence**, not new storage:

- **Display** section binds directly to the existing `RootViewModel` display
  properties (`IsDarkTheme`, `CardDetailFontSize`, `CardPreviewScale`,
  `StackDuplicates`, `ScannerFontSize`, `ScanQuality`, `DefaultScannerName`),
  reusing their live-apply + `PersistDisplaySettings()` logic. This keeps one
  source of truth and preserves live theme/font application.
- **Data Location** section hosts the existing `DataLocationViewModel` content.
- **Sales & Receipts** section binds to an extended `SalesSettingsService`.

### D3 — One receipt content model, two renderers
Define `ReceiptDocument` once and render it two ways: a WPF `FlowDocument` for
printing (mirrors the existing `PickListPrinter`) and QuestPDF for PDF export
(mirrors the existing `DecklistPdfExporter`). The width-sensitive layout is kept
deliberately simple so the two renderers stay visually close; the thermal
printer's driver governs physical paper width.

*Rejected:* a `FixedDocument` + native `DocumentViewer` preview (parent-spec
proposal). More code for a preview we're getting "for free" from the PDF export.

## 4. Data Model

New models in `OmniCard.Shared/Models`. `sales-settings.json` is extended
(existing `ForSaleLocationId` unchanged).

```csharp
class CompanyProfile
{
    string? Name;
    string? AddressLine1;
    string? AddressLine2;
    string? City;
    string? State;
    string? PostalCode;
    string? Country;
    string? Email;
    string? Phone;
    string? LogoPath;        // relative to the data directory
}

class ReceiptSettings
{
    double  WidthMm = 80;
    double  MarginMm = 4;
    double  FontPointSize = 9;
    bool    ShowPrices = true;
    string? FooterText;
    string? DefaultPrinterName;
}

class SalesSettings          // existing file, extended
{
    int?            ForSaleLocationId;     // existing
    CompanyProfile  Company = new();       // new
    ReceiptSettings Receipt = new();       // new
}
```

### `SalesSettingsService` (extended)

Keeps the existing `ForSaleLocationId` / `SetForSaleLocationId` API. Adds:

- `CompanyProfile GetCompany()` / `void SaveCompany(CompanyProfile)`
- `ReceiptSettings GetReceipt()` / `void SaveReceipt(ReceiptSettings)`
- `string SetLogo(string sourcePath)` — copies the chosen image into the data
  directory (stable filename, e.g. `company-logo<ext>`), stores the **relative**
  path in `CompanyProfile.LogoPath`, and returns it. Absolute path for load is
  resolved against `IDataPathService.DataDirectory`.

Load/save continues to round-trip the whole `SalesSettings` object through the
existing JSON read/write helpers (tolerant of a missing/old file: new fields
default).

## 5. Receipt Pipeline

### `ReceiptDocument` (Shared) — content model
```csharp
class ReceiptDocument
{
    // company header
    string? CompanyName;
    string? CompanyAddressBlock;   // pre-joined multi-line address
    string? CompanyLogoAbsolutePath;
    string? CompanyEmail;
    string? CompanyPhone;
    // order info
    string? OrderNumber;
    DateTime OrderDate;
    string? TrackingNumber;
    string? Carrier;
    // customer
    string  CustomerName;
    string? CustomerAddressBlock;
    // lines + totals
    IReadOnlyList<ReceiptLine> Lines;
    bool     ShowPrices;
    decimal  ItemsTotal;
    decimal  Shipping;             // ShippingChargedToBuyer
    decimal  GrandTotal;           // ItemsTotal + Shipping
    string?  FooterText;
    double   WidthMm;
    double   MarginMm;
    double   FontPointSize;
}

class ReceiptLine
{
    string  Name;
    string? Set;
    string? Condition;
    bool    IsFoil;
    int     Quantity;
    decimal UnitSalePrice;
    decimal LineTotal;             // Quantity * UnitSalePrice
}
```

### `ReceiptService` (Collection) — the tested seam
`ReceiptDocument BuildReceipt(int orderId)`:
1. Loads the `Order`, its `OrderLine`s (snapshots), and the `Customer`.
2. Loads `CompanyProfile` + `ReceiptSettings` from `SalesSettingsService`.
3. Assembles address blocks, maps order lines → `ReceiptLine`s, computes
   `ItemsTotal` / `GrandTotal`, resolves the logo to an absolute path, and copies
   `ShowPrices` / width / margin / font onto the document.

### `ReceiptPrinter` (OmniCard, WPF) — print
Static helper mirroring `PickListPrinter`. Builds a `FlowDocument` with
`PageWidth = mm→DIP(WidthMm)` and `PagePadding = mm→DIP(MarginMm)`, base font size
from `FontPointSize`, embeds the logo (`Image` from `CompanyLogoAbsolutePath` when
present), lays out header → order/customer → line table → totals (when
`ShowPrices`) → footer, then `PrintDialog.PrintDocument(...)`. When
`DefaultPrinterName` is set, pre-select that queue on the dialog; otherwise the
user picks. (mm→DIP = `mm / 25.4 * 96`.)

### `ReceiptPdfExporter` (OmniCard.Audit, QuestPDF) — export
`IReceiptPdfExporter.Export(ReceiptDocument, string filePath)`. Continuous page
of `WidthMm` width (`page.ContinuousSize(WidthMm, Unit.Millimetre)`),
`QuestPDF.Settings.License = Community`, same section order as the printer. Serves
as the on-screen preview when opened.

## 6. UI Wiring

### Settings tab (new)
- `SettingsView` + `SettingsViewModel` host the section selector and content pane.
- `DisplaySettingsSection` — binds to the existing `RootViewModel` display
  properties (passed in / exposed to the Settings tab's DataContext).
- `DataLocationSection` — reuses `DataLocationViewModel`.
- `SalesSettingsSection` + `SalesSettingsViewModel` — For-Sale location picker
  (StorageContainer list), Company Profile fields + logo picker
  (`OpenFileDialog` → `SalesSettingsService.SetLogo`), and Receipt Settings
  fields.

### RootView / menu cleanup
- Remove the now-duplicated View-menu display controls: theme, the three
  font/preview sliders, and stack-duplicates. The **Show pHash Preview** toggle
  is a diagnostic control (not in the migrated Display set) and **stays** in the
  View menu.
- Edit → **Data Location…** stays but selects the Settings tab (Data section).

### Pick List header
- Remove the inline For-Sale `ComboBox`; show the configured location read-only
  with a hint pointing to Settings. `SalesViewModel` reads `ForSaleLocationId`
  from settings on load; `MarkPicked` / bulk-pick behavior is unchanged.

### Orders view
- Add **Print Receipt** and **Export PDF…** commands to `OrdersViewModel`,
  enabled when an order is selected. Print → `ReceiptService.BuildReceipt` →
  `ReceiptPrinter.Print`. Export → `SaveFileDialog` →
  `ReceiptService.BuildReceipt` → `ReceiptPdfExporter.Export`.

## 7. Testing Strategy

- **`SalesSettingsService`** (xUnit, temp data dir): Company + Receipt round-trip;
  old/missing-field JSON loads with defaults; `SetLogo` copies the file and stores
  a relative path resolvable back to an absolute path.
- **`ReceiptService.BuildReceipt`** (in-memory SQLite, matching existing service
  test harness): correct assembly — line mapping incl. foil flag and snapshots,
  `ItemsTotal`/`GrandTotal`, `ShowPrices` on/off, address-block joining, logo
  path resolution, empty-optional handling.
- **`ReceiptPdfExporter`**: smoke test — non-empty bytes produced, no throw, for a
  representative `ReceiptDocument` (mirrors `DecklistPdfExporterTests`).
- **Human E2E** before merge: FlowDocument print to the thermal printer at the
  configured width; Settings page (all three sections, theme live-apply, logo
  pick); Pick List still picks with the Settings-configured For-Sale location.

## 8. Build Phasing (one spec, two green checkpoints)

1. **Settings foundation & migration** — `CompanyProfile` / `ReceiptSettings`
   models + `SalesSettingsService` extension (+ `SetLogo`); Settings tab shell +
   Display / Data Location / Sales & Receipts sections; remove duplicate
   View-menu controls; move For-Sale picker out of the Pick List header. Service
   + settings tests green.
2. **Receipt** — `ReceiptDocument` / `ReceiptLine`; `ReceiptService.BuildReceipt`;
   `ReceiptPrinter` (FlowDocument); `ReceiptPdfExporter` (QuestPDF) +
   `IReceiptPdfExporter`; Orders view Print Receipt / Export PDF wiring. Service
   + PDF smoke tests green.

## 9. Project Placement

| Artifact | Project |
|----------|---------|
| `CompanyProfile`, `ReceiptSettings`, `ReceiptDocument`, `ReceiptLine` | `OmniCard.Shared/Models` |
| `IReceiptService`, `IReceiptPdfExporter` | `OmniCard.Shared/Interfaces` |
| `ReceiptService`, `SalesSettingsService` (extend) | `OmniCard.Collection` |
| `ReceiptPdfExporter` | `OmniCard.Audit` |
| `ReceiptPrinter`, `SettingsView`/VM + sections | `OmniCard/Views/Settings`, `OmniCard/Views/Sales` |

## 10. Open Follow-ups (out of scope)

- TCGPlayer CSV import (parent spec §8.4).
- Graphical in-app receipt preview (deferred; PDF export covers it for now).
