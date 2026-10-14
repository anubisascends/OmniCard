# Scanning cards

Use the Scan page to identify cards from photos or scanner images, check each match, fix any that are wrong, and add the confirmed cards to a location in your collection.

## What the Scan page does

Open [Scan](/scan) from the navigation menu. You add pictures of your cards (from a scanner, a phone camera, or a webcam), and OmniCard identifies each one against the selected game's card catalog. Every picture becomes a row in the review list. You look over the matches, correct any mistakes, set details like condition and foil, and then add the cards to a location.

Nothing is added to your collection until you click the add button. Until then the scans live only in your browser, so you can take your time.

![The Scan page with a review list on the left and the selected card's details on the right](scanning-overview.webp)

> [!NOTE]
> If you don't see **Scan** in the menu, ask an administrator for access.

## Scan your first cards

1. Open [Scan](/scan).
2. In the top panel, choose the **Game** you are scanning.
3. Optionally set the **Condition**, **Card language** and **Foil** that most of these cards share. Each new scan starts with these values (see [Session defaults](#session-defaults)).
4. Add pictures with **Take photo**, **Use webcam** or **Add images** (see [Ways to add pictures](#ways-to-add-pictures)).
5. Wait for matching to finish. Each row shows a spinner while it is being matched, then a confidence percentage.
6. Compare **Uploaded scan** with **Matched art** for the selected card. Click **Confirm match** (or press **Enter**) when it is right. OmniCard moves on to the next card. If the match is wrong, click **Search catalog** to pick the correct card. See [Review with the keyboard](#review-with-the-keyboard).
7. Click **Add to location…** and choose where the cards are going.
8. Click **Add N confirmed cards**.

Only cards that are both **confirmed** and **checked** are added. Everything else stays in the list.

## Ways to add pictures

| Button | Best for | What happens |
|---|---|---|
| **Take photo** | Phones and tablets | Opens the device's rear camera so you can photograph one card. On a computer it opens a normal file picker instead. |
| **Use webcam** | A computer with a webcam | Opens a live camera view that finds the card and captures it automatically. |
| **Add images** | Scanner output or saved photos | Lets you pick one or many image files at once. JPEG, PNG and TIFF files are supported. |

You can mix all three in one session. New scans appear at the top of the list.

### Scanning with a webcam

1. Click **Use webcam**. The **Scan with webcam** window opens. If your browser asks for permission to use the camera, allow it.
2. If you have more than one camera, choose it from the **Camera** list.
3. Hold a card in front of the camera on a plain, contrasting background. An outline appears around the card when it is found.
4. Keep the card still. The status changes from **Point a card at the camera** to **Hold steady…**, and after about a second the card is captured: the frame flashes and the status reads **Captured ✓ — present the next card**.
5. Take the card away and present the next one. A card left sitting in view is captured only once.
6. To capture the same card again, click **Capture now**.
7. Click **Done** when you are finished.

The window shows how many cards you have captured and thumbnails of the last few. Each capture is straightened and cropped automatically, then sent for matching just like an uploaded image.

![The Scan with webcam window with a card outlined and the captured count below](scanning-webcam.png)

> [!TIP]
> Most browsers only allow webcam access when OmniCard is opened over a secure (https) address or on the same computer that runs it. If the camera won't start, use **Take photo** on a phone or **Add images** instead, or ask your administrator.

## Session defaults

The top panel controls apply to every scan you add **after** you set them:

- **Game** — which game's catalog to match against. Changing the game clears the set and language choices.
- **Sets (art fallback)** — pick one or more sets if you know what you are scanning. Matching is limited to those sets, and so is the catalog search when you correct a card. Leave it on **All sets** to search everything.
- **Condition** — NM, LP, MP, HP or DMG.
- **Card language** — **Auto (detect)** reads the language printed on Magic: The Gathering and Yu-Gi-Oh! cards. For other games, pick the language of the cards you are scanning.
- **Foil** — tick this before scanning foil cards. It marks the new scans as foil, helps matching cope with foil shine, and shows the foil market value.

Changing these later does not change cards already in the list. To change existing cards, edit them individually or in bulk (see [Card properties](#card-properties)).

## How matching works

OmniCard compares each picture with the catalog's card artwork and also reads the printed text on the card, such as the set code and collector number, to pin down the exact printing. A card scanned upside down is usually still recognized.

The colored chip on each row tells you how sure the match is:

| Chip | Meaning |
|---|---|
| Green percentage (50% or more) | A strong match. Usually correct, but still worth a glance. |
| Amber percentage (15% to 49%) | A possible match. Check it carefully. |
| Red percentage (under 15%) | A weak match. Likely wrong. |
| **No match** | Nothing close enough was found. Use **Search catalog**. |
| **Corrected** | You picked the card yourself from the catalog. |
| **Error** | The picture couldn't be processed. Try a clearer image. |

Matched cards are checked automatically; unmatched ones are not. When you confirm or correct a card, OmniCard remembers that identity, so the same card tends to match better next time.

## Review the list

On a wide screen the review list is on the left and the selected card's details are on the right. On a phone the details appear below the list.

Each row shows the scan thumbnail next to the matched artwork, the card name, set and collector number, a one-line summary of its properties (condition, language, foil, quantity and a note icon), its badges and its tags. Tags added by a [tag rule](help:tag-rules) have a sparkle icon. A green check mark before the name means you have confirmed it.

Click a row to see it in the detail panel:

- **Uploaded scan** and **Matched art** side by side, so you can compare them.
- **View scan** opens a large version of your picture so you can read small print.
- The card's name, set, collector number and rarity.
- **Confirm match** marks the match as correct (the button then reads **Looks correct**). Confirming also checks the card, then selects the next card that still needs review.
- **Search catalog** opens the correction search.
- **Exclude from commit** unchecks the card and moves on to the next card. **Include in commit** checks it again.
- **Remove** deletes the scan from the list.

![The detail panel comparing the uploaded scan with the matched art, with Confirm match and Search catalog buttons](scanning-detail-panel.webp)

### Review with the keyboard

You can review a whole list without the mouse. The bar above the list shows **Reviewed N of M**. A card counts as reviewed once you confirm, correct or exclude it.

| Key | What it does |
| --- | --- |
| **Enter** | Confirm the match and go to the next card that still needs review. On a card with no match, it opens **Search catalog** instead. |
| **↓** or **J** | Next card |
| **↑** or **K** | Previous card |
| **S** or **/** | Open **Search catalog** |
| **Esc** | Close the search |
| **X** | Exclude the card from the commit and go to the next card. Press it again to include the card. |
| **Ctrl+Z** | Undo the last confirm, correction or exclude, and go back to that card |
| **?** | Show the list of shortcuts |

"Next" follows the list's current filter and sort, skips cards you have already reviewed and cards still being matched, and starts again from the top when it reaches the end. Sorting by **Confidence**, for example, lets you confirm the strongest matches first.

In the search, type a name or collector number, press **↓** to move into the results, then **Enter** to pick one. The pick confirms the card and moves on.

When every card in the list has been reviewed, the bar changes to **Review complete**. Choose a location and add the cards as usual. Adding is never done from the keyboard alone.

> [!NOTE]
> Shortcuts don't work while you're typing in a field such as **Note** or **Tags**. Click an empty part of the page to use them again.

### Checking cards

The checkbox on each row decides whether the card is included when you add or export. Any scan can be checked, but only checked cards with a match (or a correction) are added or exported.

- **Shift**-click a second checkbox to check or uncheck every row between it and the last one you clicked.
- The header above the list has a check-all box plus **All**, **None** and **Invert**, and shows how many are checked.
- **Delete** in the list header removes every checked scan from the list, including scans that didn't match or are still matching. OmniCard asks you to confirm first, because this can't be undone.
- **Confirm N checked** in the action bar confirms every checked, matched card at once. Use it after you have eyeballed a batch of strong matches.

## Correct a wrong match

1. Select the card and click **Search catalog**.
2. Type at least two letters of the card's name, or enter its **Collector #**, or both.
3. Hover over a result's small picture to see it larger.
4. Click the correct result.

The card is now marked **Corrected**, and it is confirmed and checked. The next card that needs review is selected. Click **Cancel** to close the search without changing anything.

The search looks only in the sets chosen in **Sets (art fallback)**; the line under the search box says which. If the right card isn't listed, clear that set selection at the top of the page and search again.

> [!TIP]
> Corrected cards don't show a confidence percentage or value badges, because those come from the automatic match.

## Card properties

The **Card properties** section of the detail panel sets the details for that copy:

- **Condition** — NM, LP, MP, HP or DMG.
- **Language** — the language of this copy. A small language code (for example *JA*) appears on the row for non-English cards; it is filled in solid when the language was read from the card itself.
- **Quantity** — how many identical copies this scan represents.
- **Purchase price** — what you paid, if you want to track it.
- **Foil** — turn on for a foil copy, then optionally choose or type a **Foil type**.
- **Tags** — pick existing tags or type new ones. Tags with a sparkle icon were added by a [tag rule](help:tag-rules). Remove one to keep it off this card. To stop rule tags altogether, turn off **Apply tag rules** in the action bar.
- **Note** — free text, for example *signed, played, misprint…*

### Edit several cards at once

1. Check the cards you want to change.
2. Click **Edit** in the list header. The **Edit N selected cards** window opens.
3. Tick each property you want to apply, then set its value. Unticked properties are left alone.
4. For **Tags**, choose a **Mode**: **Add** keeps each card's existing tags and adds yours; **Replace** swaps them for yours.
5. Click **Apply to N**.

![The Edit selected cards window with Condition and Tags ticked](scanning-bulk-edit.png)

## Filter and sort the list

When the list is long, use the filter bar above it:

- **Filter by name** — shows rows whose card name contains the text.
- **Show** — **All**, **Checked only** or **Unchecked only**.
- **Min confidence %** — hides weaker matches.
- **Min value** — hides cards below a market value.
- **Sort by** — **None** (newest first), **Name**, **Confidence** or **Market value**. The arrow next to it switches between ascending and descending. Cards without a value sort to the bottom.

While a filter or sort is on, you'll see *Showing X of Y* and a **Clear** button.

> [!NOTE]
> Everything in the action bar works only on the cards you can currently see. **Confirm**, **Add**, **Export**, check-all, bulk **Edit** and **Delete** all ignore rows hidden by a filter.

## Value badges

Matched cards can show two kinds of badge:

- A **gold star** means the card is new: you don't own a copy yet.
- One to five **currency signs** (for example $$$) show the card's market value tier. More signs means a more valuable card. Hover over them to see the exact value and the tier's price range.

An administrator sets the currency and the price range for each tier in **Administration ▸ Scan Badges**. See [Administration](help:administration).

## Add cards to a location

1. Click **Add to location…** in the action bar. The **Add scanned cards to location** window opens.
2. Search or scroll the list. Locations are grouped by type and show their card count. Only locations you are allowed to change are listed.
3. Click a location to choose it. Its name now appears on the button.
4. Click **Add N confirmed cards**.

The button stays disabled until a location is chosen, at least one card is confirmed and checked, and every scan has finished matching. When the cards are added you'll see *Added N card(s) to your collection*, and those rows leave the list. Unchecked or unconfirmed scans stay so you can deal with them later.

### Create a location on the spot

If the location doesn't exist yet, you don't have to leave the page:

1. In the location window, click **New location**.
2. Enter a **New location name**. You'll be warned if *This name is already in use*.
3. Choose the **Type** (and the site, if your account can use more than one). For a deck box, also choose its game.
4. Click **Create & select**.

![The Add scanned cards to location window with the New location form open](scanning-location-picker.png)

See [Locations](help:locations) for more about location types and sites.

## Export scans without adding them

You can download your scans as a file without putting anything in your collection, for example to price a stack before you buy it.

1. Check the cards you want to export. Confirming isn't required.
2. Click **Export (N)** and choose a format: **OmniCard (full detail)**, **TCGplayer**, **Moxfield**, **ManaBox**, **Archidekt**, **Deckbox**, **Dragon Shield**, **Card Price Ticker** or **Text list (.txt)**. Moxfield, ManaBox, Archidekt and Deckbox are marked *MTG only* and are available only for Magic: The Gathering.
3. Or choose **Copy as text list** to copy lines such as *4 Lightning Bolt (2X2) 117* to the clipboard, ready to paste into a deck builder.
4. Or choose **Export several formats (.zip)…**, tick the formats you want, and click **Download N formats**. Your choice is remembered for next time in this browser.

Exported scans stay in the list, so you can still add them to a location later.

> [!NOTE]
> If you don't see **Export**, ask an administrator for access.

## Pick up where you left off

Your scan list is saved in this browser as you work. If you refresh the page, close the tab or your browser crashes, the next visit to Scan shows *Restored N scans from your last session*. Scans that were still matching are matched again automatically. Click **Discard** in that message to throw the restored scans away.

The saved list belongs to this browser on this device only. It isn't kept in a private or incognito window, and another person or device won't see it.

## Game-specific notes

- **Magic: The Gathering — The List.** When a scanned card is a reprint from *The List*, OmniCard matches it to that cheaper printing and shows a **The List** chip. If it shows **The List?** instead, the reprint was detected but its printing wasn't found in the catalog, so the printing and price shown may be the more expensive original. Check it before adding.
- **Yu-Gi-Oh! editions.** When the scan shows *1st Edition* or *Limited Edition* text, that edition appears as a chip in the detail panel for reference.
- **Riftbound Battlefields.** Landscape Battlefield cards can be scanned sideways; OmniCard turns them the right way automatically.
- **Language detection.** With **Card language** set to **Auto (detect)**, the printed language is read from Magic: The Gathering and Yu-Gi-Oh! cards.

## Tips and troubleshooting

- **Lots of wrong matches?** Choose the set or sets you are scanning in **Sets (art fallback)** before adding pictures. Make sure the right **Game** is selected.
- **Foils matching badly?** Tick **Foil** before you scan them.
- **Can't read the card in the thumbnail?** Use **View scan** to open the full picture.
- **The add button stays grey.** Check that a location is chosen, that matching has finished for every row, and that at least one card is both confirmed and checked and visible under the current filter.
- **A scan shows Error.** Remove it and try a sharper, well-lit picture.
- Scanner output can also be processed in the background without opening the browser. See [Scan batches](help:scan-batches).
- To check that a location holds exactly what you think it does, scan it in an audit. See [Auditing a location](help:location-audit).
- Added cards appear in your [collection](help:collection) right away.
