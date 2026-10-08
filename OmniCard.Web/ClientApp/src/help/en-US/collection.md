# Collection

The Collection page lists every single card you own across all your storage locations. Use it to search, sort, edit, move, list for sale, export, and check a decklist against what you have.

## What the Collection page shows

Open [Collection](/collection) from the navigation menu. The page has three parts:

- A **search box** at the top. Type a search and press **Enter** to filter the list.
- The **view button** beside the search box, for switching between saved layouts of the page. See [Saved views](help:saved-views).
- A toolbar with the **Stack duplicates** switch and the **Columns** and **Select** buttons.
- The card list itself, one card (or stack of identical cards) per row.

The game selector in the top bar decides which game's cards you see. Choose a single game, or **All Games** to see everything at once.

![The Collection page with the search box, toolbar and card list](collection-overview.png)

> [!NOTE]
> You only see cards in sites you have access to. If cards you expect are missing, ask an administrator whether you can view that site. See [Locations](help:locations).

### Columns

| Column | What it shows |
|---|---|
| Name | Card name. Hover over it to see the card art. |
| Set | Set code. |
| No. | Collector number. |
| Rarity | Printed rarity. |
| Cond | Condition: NM, LP, MP, HP or DMG. |
| Lang | A language badge for non-English copies. Blank means English. |
| Foil | A check mark for foil copies. |
| Qty | How many copies the row holds. |
| Market | Current market price per copy, in US dollars. |
| Status | **Listed** or **Picked** if the card is listed for sale. |
| Location | The storage location the card is in. |

Hover over a **Listed** or **Picked** badge to see a reminder that you change listings from **Sales ▸ Listings**.

Click **Columns** to hide columns or change their order. See [Choose columns](help:saved-views#choose-columns).

## Search your collection

1. Click in the search box.
2. Type a card name, or a search using fields such as `t:creature`, `set:dom` or `tag:trade`.
3. Press **Enter**.

Plain words match the card name, so `bolt` finds Lightning Bolt. Combine terms with spaces, use `or` for alternatives, and put `-` in front of a term to exclude it. For example, `t:dragon -is:foil` finds non-foil dragons.

Click the **?** icon at the right of the search box to see the fields available for the selected game, with an example for each.

For the complete list of fields and operators for every game, see [Search syntax](help:search-syntax).

To clear a search, delete the text and press **Enter** again.

## Sort the list

Click a column header to sort by that column. Click it again to reverse the order. Sorting covers your whole collection, not just the page you're looking at, so sorting by **Market** from highest to lowest shows your most valuable cards first.

The list is sorted by **Name** until you choose another column. The **Status** column can't be sorted.

Use the controls at the bottom of the list to move between pages and to show 25, 50 or 100 rows per page.

To keep a sort, search and column layout for next time, save it as a view. See [Saved views](help:saved-views).

## Stack duplicates

The **Stack duplicates** switch is on by default. A [saved view](help:saved-views) remembers the setting. With **Default view**, OmniCard remembers your last choice.

- **On:** identical cards (same name, set, collector number and foil) appear as one row. **Qty** shows the total, and the name shows how many separate entries were combined, for example *· 3 printings*.
- **Off:** every entry appears on its own row, even if it's the same printing stored in two places.

> [!TIP]
> Turn **Stack duplicates** off when you want to edit or move one particular copy. Clicking a stacked row opens just one of the combined entries.

## Preview card art

Hover over a card's name to see a larger image of the card. Foil cards show an animated rainbow sheen so you can tell them apart at a glance.

To make the preview bigger or smaller, go to **Administration ▸ Appearance** and adjust the card preview size.

## View and edit a card

Click any row (when you're not in Select mode) to open the card details panel on the right.

![The card details panel with condition, language, foil, quantity and location](collection-card-details.png)

The top of the panel shows the card name, set, collector number, rarity and art. A chip shows the current **Market** price, and another chip appears if the card is **Listed for sale** or **Picked for sale**.

### Change a card's details

1. Click the card to open the details panel.
2. Change any of the fields below.
3. Click **Save**. Click **Cancel** to close without saving.

| Field | What it does |
|---|---|
| Condition | NM, LP, MP, HP or DMG. |
| Language | The language of this copy. The list shows the languages available for the card's game. |
| Foil | Turn on for a foil copy. |
| Quantity | How many copies this entry holds (at least 1). |
| Purchase price | What you paid per copy. Optional. |
| Note | Free text, for example *signed, played, misprint*. |
| Location | Where the card is stored. Click **Change** to pick another location. |
| Tags | Your own labels. Pick an existing tag or type a new one and press Enter. |

Moving a card with **Change** takes effect when you click **Save**. Tags you add here can be searched with `tag:`. See [Search syntax](help:search-syntax).

### Pick a location

When you click **Change** (or **Move to…** for several cards), the location picker opens.

- Type in **Search locations…** to filter the list. Locations are grouped by type.
- Click a location to choose it.
- Click **New location** to create one without leaving the picker. Enter a **New location name**, choose the type (and the site, if you can add to more than one), then click **Create & select**. A new deck box also needs its game.

A deck box that holds a different game is shown but can't be chosen. For more about locations and sites, see [Locations](help:locations) and [Deck boxes](help:deck-boxes).

### Read-only cards

If a card is in a site you can only view, the panel shows a message and every change is disabled. Ask an administrator for write access to that site if you need to edit it.

### Split a stack

When an entry holds more than one copy, the panel shows **Split stack**. Splitting is useful when each copy needs its own binder pocket or its own sale listing.

1. Click **Split stack**.
2. Choose one of these:
   - Enter the number of **Copies to move to a new stack**, then click **Split stack**.
   - Click **Split into N singles** to turn every copy into its own entry.
3. The new entries stay in the same location. In a binder, they go to the binder's Unplaced pool. See [Binders](help:binders).

You can't split a card that's listed for sale. Unlist it from **Sales ▸ Listings** first.

### Add a card to a trade

Click **Add to trade** to put the card into your current trade. A message confirms it was added. Finish the trade on the [Trades](/trades) page. See [Trades](help:trades).

### Delete a card

Click **Delete** at the bottom of the panel and confirm. This removes the card from your collection and can't be undone.

## List a card for sale

From the card details panel:

1. Click **List for sale**.
2. If the entry holds more than one copy, set **Quantity**. Listing fewer than you have splits those copies off into their own entry.
3. Check the **Price**. It starts at the current market price.
4. Choose a **Channel**: Manual, TCGplayer or eBay.
5. Add a **Note** if you like.
6. Click **List for sale**.

![The List for sale dialog with the eBay listing section shown](collection-list-for-sale.png)

If the card is already listed, the button reads **Already listed for sale** and is disabled.

### List on eBay

Choose **eBay** as the channel to publish the card to eBay at the same time. An **eBay listing** section appears with a **Listing title** (up to 80 characters), **Description**, **Condition**, **Listing type** (Fixed price or Auction, with an **Auction duration**), and **eBay category**. OmniCard suggests a title, description and category for you. Click **List on eBay** to publish.

Your eBay account must be connected and set up first. If it isn't, the dialog shows a warning. If the card is listed in OmniCard but eBay rejects it, the error is shown and the local listing is kept so you can fix the problem and try again. See [eBay](help:ebay).

For managing listings, picking and orders, see [Sales](help:sales).

## Work with several cards at once

1. Click **Select**. Checkboxes appear next to each row.
2. Tick the cards you want. The header checkbox selects every card on the current page.
3. Use the buttons that appear in the toolbar. The toolbar shows how many cards are selected.
4. Click **Done** to leave Select mode.

![Select mode with several cards ticked and the bulk action buttons showing](collection-select-actions.png)

A selected stacked row counts every entry in the stack.

| Button | What it does |
|---|---|
| **Move to…** | Moves all selected cards to one location. |
| **Bulk edit** | Changes properties on all selected cards. |
| **List for sale** | Lists all selected cards for sale at their market price. |
| **Export CSV** | Downloads the selected cards as a file. |
| **Delete** | Deletes the selected cards after you confirm. This can't be undone. |

### Bulk edit

In the **Bulk edit** dialog, tick each property you want to change, then set its value. Unticked properties are left alone. You can set **Condition**, **Language**, **Foil** (foil or non-foil), **Quantity**, **Purchase price**, **Note** and **Tags**.

For tags, choose **Add to existing** to add tags, or **Replace all** to overwrite every selected card's tags. Replacing with no tags clears them. Click **Apply to N** to save.

### Bulk list for sale

Each selected card is listed as a whole entry at its current market price. Pick a **Channel**, add an optional **Note**, and click **List for sale**. Cards that are already listed are skipped, and the dialog tells you how many. Adjust individual prices afterwards on **Sales ▸ Listings**.

> [!NOTE]
> Bulk listing records the listings in OmniCard. To publish a card to eBay, list it on its own from the card details panel with the eBay channel.

### Export selected cards

Click **Export CSV** and choose a format:

- OmniCard (full detail)
- TCGplayer
- Moxfield
- ManaBox
- Archidekt
- Deckbox
- Dragon Shield
- Card Price Ticker
- Text list (.txt)

The file downloads to your computer. To bring cards in from a file instead, see [Importing](help:importing).

## Check a decklist against your collection

**Check decklist** shows which cards from a deck you already own, exactly where to find them, and what's missing.

1. Click **Check decklist** at the top right of the page.
2. Choose the **Game**.
3. Paste a Moxfield or Archidekt link into **Decklist URL**, or paste the list into **Decklist text**, one card per line (for example `4 Lightning Bolt`).
4. Click **Check**.

![The Check a decklist dialog showing owned and missing counts and the pull list](collection-decklist-check.png)

The results show the deck name and three chips: how many cards you own, how many are missing, and the estimated cost to complete the deck.

### To pull

The **To pull** tab lists each card you own and the copies to take, with set, collector number, condition, a ✦ for foil, and where each copy is (location, section, page and slot).

OmniCard picks copies in this order of preference: copies that aren't listed for sale, then copies that aren't already in another deck box, then the exact printing the deck asks for, then the same set. Copies in locations marked to be ignored for lists are never picked. See [Lists](help:lists).

Chips flag copies that are **Listed for sale** or **In a deck box**.

### Missing

The **Missing** tab lists cards you don't have enough of, with the cost to buy them. The most expensive cards are listed first.

### Print and move

- **Print pull list** downloads a PDF checklist of the cards to pull and where they are.
- **Print missing list** downloads a PDF shopping list of the missing cards with their prices.
- **Move to deck box…** moves every picked copy into a deck box you choose (or create). This is only available once you own every card in the deck and have permission to move cards. If some copies are listed for sale or are in another deck box, a message warns you before you move them.

After a move, the check runs again so the pull list shows the cards' new location. The dialog keeps its results while you close it to look at your collection.

For deck boxes and saved card lists, see [Deck boxes](help:deck-boxes) and [Lists](help:lists).

## Tips and troubleshooting

- **A search finds nothing.** Check the game selector in the top bar. A search only covers the selected game, unless **All Games** is selected.
- **Market is blank.** No price is available for that printing yet. Prices come from the game's price data and update when the card catalog is refreshed.
- **I can't click List for sale or Split stack.** The card is already listed. Unlist it from **Sales ▸ Listings** first.
- **Buttons are greyed out or missing.** You may not have permission for that action, or the card is in a read-only site. Ask an administrator for access.
- To view one location's cards only, open it from [Locations](help:locations). The same card list and search work there.
