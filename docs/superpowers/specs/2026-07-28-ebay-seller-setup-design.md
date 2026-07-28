# eBay Seller Setup — Design

**Date:** 2026-07-28
**Status:** Approved (pending spec review)
**Branch:** `feat/ebay-seller-setup`

## Problem

OAuth connection, token persistence, and eBay **offer creation** now work (see commit
`df88379`). Publishing a listing still fails because the eBay sandbox seller account is
not provisioned for it:

- `errorId 20403 — "User is not eligible for Business Policy."` The seller is not opted
  into Business Policies, so the app cannot fetch or attach payment/return/fulfillment
  policies; the offer's `listingPolicies` are empty.
- `errorId 25002 — "No <Item.Country> exists…"` on publish. The Inventory API derives the
  item country from a **merchant/inventory location**, which the account lacks and the app
  never sends (`merchantLocationKey`).

Both are prerequisites for `publishOffer` to succeed. The account-side actions (opt-in,
policy creation, a seller address) can be automated through the eBay Account/Inventory
APIs, and the offer must reference the created location + policy IDs.

## Goal

A user-triggered, idempotent "Run eBay Setup" action that provisions the seller account
(Business Policies opt-in, inventory location, default policies) and persists the resulting
identifiers, so that creating a listing produces a **published** eBay listing.

Non-goals: a full policy editor, international shipping matrices, multiple locations,
production managed-payments onboarding.

## Decisions (from brainstorming)

- **Trigger:** a dedicated "Run eBay Setup" action (not auto-on-connect, not lazy-on-listing).
- **Location address:** dedicated eBay-specific address fields (separate from the
  Sales `CompanyProfile`).
- **Policy configurability:** a few key fields (shipping cost/free, handling time, returns
  accepted + window); everything else uses sensible defaults.
- **Payment-policy failure is non-fatal** (sandbox managed-payments caveat).
- **UI home:** a new "eBay Selling" section in the existing Settings dialog.

## Architecture

Three new, isolated units plus small integration edits. Rationale: `EbayListingService`
is already ~400 lines and doing too much; setup and settings are separate concerns with
their own interfaces and tests.

```
Settings dialog
  └─ EbaySellingSettingsView / …ViewModel        (address + policy fields, "Run Setup" button)
        │ reads/writes
        ▼
  IEbaySellingSettingsService  ──►  ebay-selling.json   (mirrors SalesSettingsService)
        ▲
        │ reads settings, writes back created IDs
  IEbaySellerSetupService  ──►  eBay Account + Inventory APIs   (opt-in, location, policies)
        ▲
        │ merchantLocationKey + policy IDs
  EbayListingService.BuildOffer / CreateListingAsync
```

### 1. Persistence — `EbaySellingSettings` + `IEbaySellingSettingsService`

New model `OmniCard.Shared/Models/EbaySellingSettings.cs`:

```csharp
public class EbaySellingSettings
{
    // Location
    public string MerchantLocationKey { get; set; } = "omnicard-primary"; // stable
    public string? LocationName { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }   // ISO 3166-1 alpha-2, e.g. "US"
    public string? Phone { get; set; }

    // Shipping (fulfillment) policy inputs
    public bool FreeShipping { get; set; } = true;
    public decimal ShippingCost { get; set; }          // used when FreeShipping == false
    public int HandlingTimeDays { get; set; } = 1;

    // Return policy inputs
    public bool ReturnsAccepted { get; set; } = true;
    public int ReturnWindowDays { get; set; } = 30;    // 30 or 60
    public ReturnShippingPayer ReturnShippingPaidBy { get; set; } = ReturnShippingPayer.Buyer;

    // Results (written by setup)
    public string? FulfillmentPolicyId { get; set; }
    public string? PaymentPolicyId { get; set; }
    public string? ReturnPolicyId { get; set; }
    public DateTime? SetupCompletedAt { get; set; }
}

public enum ReturnShippingPayer { Buyer, Seller }
```

`IEbaySellingSettingsService` with `Get()` / `Save(EbaySellingSettings)`, implemented in
`OmniCard.Collection` (alongside `SalesSettingsService`), persisting to
`ebay-selling.json` in `DataDirectory` using the same load/save/JSON pattern.

`IsSetupComplete(settings)` helper = has `MerchantLocationKey` provisioned AND non-empty
`FulfillmentPolicyId` and `ReturnPolicyId` (payment optional — see §5).

### 2. Orchestration — `IEbaySellerSetupService`

In `OmniCard.eBay`. Depends on `IEbayAuthService`, `IHttpClientFactory`,
`IEbaySellingSettingsService`, `ILogger`. Marketplace fixed to `EBAY_US`.

```csharp
Task<EbaySetupResult> RunSetupAsync(IProgress<string>? progress = null);
```

`EbaySetupResult` = ordered list of `EbaySetupStep { Name, Status (Ok|SkippedExisting|Failed), Message }`
plus overall `Success`. Steps (each idempotent, each logs request + response body):

1. **Opt into Business Policies** — `POST /sell/account/v1/program/opt_in`
   `{ "programType": "SELLING_POLICY_MANAGEMENT" }`. Treat already-opted-in (the
   "already opted in" error / non-success that indicates existing enrolment) as `Ok`.
2. **Ensure inventory location** — `GET /sell/inventory/v1/location/{key}`; on 404,
   `POST /sell/inventory/v1/location/{key}` with `location.address` from settings
   (country + postalCode required). Validates required address fields first; missing →
   step `Failed` with a clear message. Fixes error 25002.
3. **Ensure fulfillment policy** — `GET …/fulfillment_policy?marketplace_id=EBAY_US`,
   find one named `"OmniCard Default"`; if absent `POST` it (handling time, free or
   flat-rate domestic shipping from settings). Store `FulfillmentPolicyId`.
4. **Ensure payment policy** — same pattern. On failure → step `Failed`, **continue**.
5. **Ensure return policy** — same pattern (returns accepted, window, who pays). Store id.

After steps, persist discovered/created IDs and (if location + fulfillment + return
succeeded) `SetupCompletedAt`. Re-running detects existing resources and only fills gaps.

All mutating Inventory calls reuse the `Content-Language: en-US` helper (already added).

### 3. UI — "eBay Selling" settings section

`EbaySellingSettingsView(.xaml/.cs)` + `EbaySellingSettingsViewModel`, added to the
Settings dialog next to `SalesSettingsView` (registered in `SettingsViewModel` and DI the
same way). Contents:

- Address fields (Name, Address 1/2, City, State, Postal code, Country, Phone).
- Shipping: "Free shipping" toggle, Shipping cost (enabled when not free), Handling days.
- Returns: "Accept returns" toggle, Window (30/60), Return shipping paid by (Buyer/Seller).
- **Run eBay Setup** button (disabled unless connected + required address fields present).
  Runs `RunSetupAsync` with an `IProgress<string>` that appends to a status list; on
  completion shows each step's outcome and an overall summary.
- Current-state banner: "Setup complete" (with the stored IDs) or what remains.

Follows the app's dark-theme text rule (explicit `Foreground` on `TextBlock`s).

### 4. Listing integration

- `BuildOffer` gains `merchantLocationKey = settings.MerchantLocationKey`.
- `CreateListingAsync` loads `EbaySellingSettings`; if `EbayListingOptions` policy IDs are
  empty, fall back to the stored `FulfillmentPolicyId/PaymentPolicyId/ReturnPolicyId`.
- Pre-flight guard: if setup is incomplete (no location or no fulfillment/return policy),
  return a specific failure — surfaced by `EbayListingViewModel` as
  *"Run eBay Setup first (Settings ▸ eBay Selling)."* — instead of a raw API error.
- `EbayListingService` takes a new `IEbaySellingSettingsService` dependency.

### 5. Error handling

- Every setup step captures eBay's response body into its `Message`; the catch-all logs
  with the card/step context.
- **Payment policy** failure is non-fatal: setup still stores the other IDs and reports the
  payment step as `Failed` with eBay's message + hint ("finish managed-payments setup on
  eBay's site, then re-run"). `IsSetupComplete` does not require a payment policy; the
  offer omits `paymentPolicyId` when absent.
- Setup is fully re-runnable; nothing is destructive.

### 6. Testing

- `EbaySellerSetupServiceTests` with a routing fake `HttpMessageHandler` (responds per
  path/method): opt-in ok; location 404-then-create vs already-exists; each policy
  create-vs-found; payment-policy failure leaves overall setup usable; stored IDs written
  back; `Content-Language` present on mutating calls; missing-address → location step fails
  with clear message.
- `EbaySellingSettingsServiceTests`: load defaults when no file, save/reload round-trip,
  corrupt-file falls back to defaults.
- `EbayListingServiceTests`: offer includes `merchantLocationKey` and falls back to stored
  policy IDs; incomplete-setup guard returns the specific failure.

## Files

New:
- `OmniCard.Shared/Models/EbaySellingSettings.cs`
- `OmniCard.Shared/Interfaces/IEbaySellingSettingsService.cs`
- `OmniCard.Shared/Interfaces/IEbaySellerSetupService.cs` (+ `EbaySetupResult`/`EbaySetupStep`)
- `OmniCard.Collection/EbaySellingSettingsService.cs`
- `OmniCard.eBay/EbaySellerSetupService.cs`
- `OmniCard/Views/Settings/EbaySellingSettingsView.xaml(.cs)` + `EbaySellingSettingsViewModel.cs`
- Test files as in §6.

Edited:
- `OmniCard.eBay/EbayListingService.cs` (merchantLocationKey, policy fallback, guard, new dep)
- `OmniCard/Views/Settings/SettingsViewModel.cs` + `SettingsView.xaml` (host new section)
- `OmniCard/App.xaml.cs` (DI registrations)

## Open risks

- Sandbox managed-payments may block payment-policy creation entirely — handled as
  non-fatal, but a fully-published listing may still need a manual eBay-side step.
- eBay's exact "already opted in" response shape is confirmed at implementation time via
  the logged response body; the opt-in step tolerates both success and already-enrolled.
