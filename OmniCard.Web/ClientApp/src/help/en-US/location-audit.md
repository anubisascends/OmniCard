# Auditing a location

An audit checks a location against what is physically in it. You scan every card that is really there, and OmniCard updates the location to match: cards you scanned are kept, cards you didn't scan are removed, and new cards are added.

## When to audit

Use an audit when a location's contents in OmniCard may have drifted from reality, for example:

- A box or binder has been sorted, traded from, or reorganized without updating OmniCard.
- You want to verify a deck box before a trade or sale.
- You are cleaning up after an import and want the scan to be the final word.

If you only want to add cards to a location, use the normal [Scan](help:scanning) page instead. An audit can delete cards.

## Start an audit

1. Open [Locations](/locations) and click the location you want to audit.
2. On the location's page, click **Audit** (next to **Import** and **Add card**).

The audit page opens, titled *Audit: location name*. A banner reminds you that committing makes this scan the source of truth for the location.

![A location page with the Audit button next to Import and Add card](location-audit-button.png)

> [!NOTE]
> The **Audit** button appears only if you are allowed to delete cards and can change the location's site. If you don't see it, ask an administrator for access.

## Scan the location

The audit page works just like the [Scan](help:scanning) page, except that the target location is locked: instead of **Add to location…** you see the location's name with a lock icon.

1. Choose the **Game**, and if you like the **Sets (art fallback)**, **Condition**, **Card language** and **Foil** defaults.
2. Add a picture of every card in the location with **Take photo**, **Use webcam** or **Add images**.
3. Review each match. **Confirm match** the correct ones and use **Search catalog** to fix the wrong ones. See [Review the list](help:scanning#review-the-list) and [Correct a wrong match](help:scanning#correct-a-wrong-match).
4. Set each card's **Condition** and **Foil** to what you see. These overwrite the stored values for matching cards (see below).
5. When every card is confirmed and checked, click **Commit audit (N cards)**.

![The audit page locked to a location, with the Commit audit button in the action bar](location-audit-page.webp)

Only confirmed and checked scans count toward the audit. **Commit audit** stays disabled until every scan has finished matching.

> [!WARNING]
> Any card in the location that isn't among the confirmed, checked scans is **deleted from your collection** when you commit (it isn't moved anywhere else). Before committing, make sure you scanned everything, that every card you want to keep is confirmed and checked, and that no filter is hiding rows: the commit only counts cards visible in the list. Click **Clear** on the filter bar first if you used one.

## What committing does

When you commit, OmniCard compares the scan with the location card by card:

| Result | What happens |
|---|---|
| **Matched** | The card was in the location and you scanned it. It stays, and its condition and foil are updated from the scan. |
| **Not found** | The card was in the location but you didn't scan it. It is deleted from your collection. |
| **Added** | You scanned a card that wasn't recorded in the location. It is added as a new card. |

A few details:

- Cards are compared by printing. A foil and a non-foil copy of the same printing count as the same card, and the scan decides whether it is foil.
- Quantities are compared too. If the location had three copies and you scanned two, one copy is removed. If you scanned four, one is added.
- Sealed products stored in the location are not touched by an audit.
- A deck box set to one game won't accept cards from another game; the commit is refused if you scanned any.

## The audit summary

After the commit, you return to the location's page and the **Audit complete** window opens. It starts with a one-line count, for example *12 matched, 2 added, 3 not found (removed)*, and notes how many matched cards had their condition or foil overwritten.

Below that are three sections you can expand:

- **Matched** — cards that were found.
- **Not found — removed** — cards that were removed because you didn't scan them.
- **Added** — new cards added from the scan.

Each line shows the card name, set and collector number, condition, foil, and quantity. Click **Done** to close the window.

![The Audit complete window with the Matched, Not found and Added sections](location-audit-summary.png)

## Pausing an audit

Your audit scans are saved in this browser as you work, separately for each location and separately from the main Scan page. If you leave the page or refresh it, open **Audit** on the same location again and you'll see *Restored N scans from your last session*. Click **Discard** in that message to start the audit over.

As on the Scan page, the saved scans stay in this browser on this device only, and aren't kept in a private or incognito window.

## Tips

- Audit one location at a time, and keep the cards you have scanned separate from the ones you haven't.
- For a large location, sort by **Confidence** to review the weakest matches first.
- You can **Export** an audit's scans without committing, for example to keep a record of what was in the box. See [Export scans without adding them](help:scanning#export-scans-without-adding-them).
- To find where a card is stored before you audit, use [Collection](help:collection). For more on locations, see [Locations](help:locations).
