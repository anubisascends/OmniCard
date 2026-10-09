# Importing and exporting

Bring cards into OmniCard from a CSV file or a Moxfield or Archidekt deck, and export your collection to CSV for other apps. Everything is on the Import / Export page.

## What this page is for

The **Import / Export** page has three parts:

- **Import collection (CSV)**: add cards from a collection spreadsheet that OmniCard or another app exported.
- **Import from Moxfield / Archidekt**: add every card in a public deck to a location, as cards you own.
- **Export collection (CSV)**: download your collection in a format another app can read.

Open it from **Import** in the navigation menu, or go to [Import](/import).

![The Import / Export page with the CSV import, deck URL import, and export sections](importing-page.png)

> [!NOTE]
> If you don't see **Import** in the menu, ask an administrator for access. You can import only into locations at sites you're allowed to change.

## Import a CSV file

1. Open [Import](/import).
2. Under **Import collection (CSV)**, click **Choose CSV file** and pick your file. The button changes to show the file's name.
3. Leave **Skip duplicates already in collection** ticked to avoid adding cards you already have. See **Duplicates** below.
4. Optionally, choose a **Target location (optional)** for the cards. If you leave it on **— none —**, the cards are added without a location.
5. Click **Import**.

When the import finishes, a message shows how many rows were imported, out of how many, and which format OmniCard detected. Rows that couldn't be read are skipped and counted as warnings.

### Supported CSV formats

OmniCard reads the file's column headings to tell the format apart. You don't need to choose a format.

| Format | Where it comes from | Games |
|---|---|---|
| App-native | OmniCard's own export | All games |
| TCGplayer | TCGplayer collection export | Magic: The Gathering |
| Moxfield | Moxfield collection export | Magic: The Gathering |
| ManaBox | ManaBox collection export | Magic: The Gathering |

If OmniCard doesn't recognize the file, nothing is imported. Export the file again from the other app, and don't edit its column headings.

### How rows are read

- **Quantity**: a blank quantity counts as 1.
- **Condition**: codes like NM, LP, MP, HP, and DMG work, and so do full names like *Near Mint* or *Lightly Played*, and ManaBox spellings like *near_mint*. A blank or unrecognized condition is imported as NM.
- **Foil**: foil cards get the game's standard foil finish unless the file names a finish.
- **Language**: a *Language* column is read if the file has one. Codes like `ja` and names like *Japanese* both work. A blank or unrecognized language is imported as English.
- **Purchase price**: kept when the file includes it.

### Duplicates

With **Skip duplicates already in collection** ticked, OmniCard skips a row when you already own a copy of the same printing, with the same finish, condition, and language, anywhere in your collection. It skips the whole row, not just the extra copies. For example, if you own one copy of a card and the file lists four, none of the four are added.

Untick the option to add every row as new cards, even when you already own matching copies. That's useful when a file lists new copies of cards you already have.

> [!WARNING]
> If you import the same file twice with **Skip duplicates already in collection** unticked, every card is added twice.

## Import a deck from Moxfield or Archidekt

This adds every card in a public Moxfield or Archidekt deck to one location, as cards you own. Use it when you've built a deck in real life and want it in your collection. These sites are for Magic: The Gathering only.

1. Under **Import from Moxfield / Archidekt**, paste the deck's address into **Deck URL**.
2. Choose a **Target location**. This is required.
3. Choose the **Condition** for all the cards (NM by default).
4. Optionally, tick **Skip duplicates already in collection**. It's unticked by default for deck imports.
5. Click **Import**.

OmniCard keeps each card's exact printing and its foil or etched finish from the deck. When a deck lists the same card more than once, the copies are combined into one stack.

The result message shows:

- How many cards were imported, out of how many in the deck.
- How many were skipped as duplicates, if you ticked that option.
- Cards whose exact printing wasn't in the catalog. OmniCard used the cheapest printing of the same card instead, so check these later.
- Cards that couldn't be found at all. Add these by hand.

![A finished deck URL import showing substituted and unmatched cards](importing-url-result.png)

> [!TIP]
> If you want to track a deck without adding the cards to your collection, use [Lists](help:lists) instead. Lists show what you own and what you need to buy.

## Export your collection

1. Choose a game with the game selector at the top of the app, or choose **All Games** to export everything.
2. Under **Export collection (CSV)**, click a format. The file downloads right away.

| Format | Use it for |
|---|---|
| **App-native** | A full OmniCard backup, or moving cards between OmniCard installs |
| **TCGplayer** | TCGplayer |
| **Moxfield** | Moxfield (Magic only) |
| **Manabox** | ManaBox (Magic only) |
| **Archidekt** | Archidekt (Magic only) |
| **Deckbox** | Deckbox (Magic only) |
| **Dragon Shield** | Dragon Shield card manager |
| **Text list** | A plain list, one line per card, that you can paste into most deck builders |

## Tags from tag rules

If an administrator has set up [tag rules](help:tag-rules), the CSV and deck imports tag matching new cards as they're added. The result message says how many cards were tagged.

## Other ways to import

- **Into one location, all or nothing**: open a location and click **Import**. You can use a CSV file or a deck URL. Every card goes into that location, and if any line has a problem, nothing is imported and each problem is listed so you can fix it. See [Locations](help:locations).
- **Checking a decklist**: to see which cards in a decklist you own and which are missing, without importing anything, use **Check decklist** on the Collection page. See [Collection](help:collection).
- **Saving a deck to build later**: import its URL on the Lists page. See [Lists](help:lists).
- **Sales orders**: order CSV files, such as TCGplayer order exports, are imported from **Sales ▸ Orders** with **Import CSV**. See [Sales](help:sales).
- **Scanning cards**: to add physical cards with a scanner or camera, see [Scanning](help:scanning).

## Troubleshooting

- **"No importable rows found."**: the file's column headings don't match a supported format, or no row could be read. Export it again from the original app.
- **Fewer cards than expected**: **Skip duplicates already in collection** may have skipped rows you already own. Import again with it unticked, but only for the rows you need, or you'll add copies twice.
- **"Couldn't fetch that deck URL"**: make sure the deck is public, and that the address is a Moxfield or Archidekt deck page.
- **A card shows the wrong printing after a deck import**: its exact printing probably wasn't in the catalog, so the cheapest printing was used. The result message lists these cards. To fix one, see [Collection](help:collection).

For other problems, see [Troubleshooting](help:troubleshooting).
