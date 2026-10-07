# Lists

Lists are saved card lists, such as a deck you want to build or cards you plan to buy. OmniCard shows which cards you already own and where they are, then helps you pull, buy, and put them away.

## What lists are for

A list is a set of cards for one game, each with a quantity. A list never changes your collection by itself. It's a plan. For every card on the list, OmniCard checks your collection and shows how many copies you already own. It also shows what the rest would cost to buy.

When the deck comes together, use **Put cards away**. OmniCard moves the copies you own into the deck's location, and it adds the copies you bought as new cards. You don't have to do any of this card by card.

Open lists from **Lists** in the navigation menu, or go to [Lists](/lists).

![The Lists page with a list selected, showing owned counts and the Put cards away panel](lists-overview.png)

> [!NOTE]
> If you don't see **Lists** in the menu, or some buttons are missing, ask an administrator for access.

## Create a list

1. Open [Lists](/lists).
2. Pick the **Game** at the top left. Each list belongs to one game, and the page shows only lists for the game you pick.
3. Type a name in **New list name**.
4. Click **Create**.

The new list appears as a chip under the top panel and opens right away. Each chip shows the list's name and how many cards it holds. Click a chip to open that list.

### Rename or delete a list

- To rename the open list, click **Rename** above it and type the new name.
- To delete a list, click the delete icon on its chip and confirm. Deleting a list doesn't change your collection.

## Import a list from Moxfield or Archidekt

You can turn a public Moxfield or Archidekt deck into a new list.

1. Open [Lists](/lists) and pick the **Game**.
2. Under **or import from a URL**, paste the deck's address into **Moxfield / Archidekt deck URL**.
3. Optionally, choose a card language next to it. Leave it on **Any language** to count copies in any language. See [Set the card language](#set-the-card-language).
4. Click **Import as new list**, or press Enter.

OmniCard names the list after the deck and opens it. A message tells you how many cards were added. If some cards couldn't be matched to the catalog, they're named in the message so you can add them by hand.

The list remembers the deck's address, so you can update it later when the deck changes. See [Update a list from its URL](#update-a-list-from-its-url).

### Add a deck to an existing list

To add a whole deck to the list that's already open, paste its address into **Add from URL (Moxfield / Archidekt)** in the list, then click **Add**.

## Add cards by hand

1. Open the list and click **Add card**.
2. In the **Add card to list** dialog, check the **Game**, then search by **Name**. You can narrow the search with **Set** and **Collector #**. If the list has a card language, a switch such as **Only Japanese cards** limits both groups of results to that language. Turn it off to see every language.
3. Results come in two groups:
   - **In your collection**: copies you already own. Each shows its set, number, condition, and location. Click one to add that copy to the list. The copy doesn't move yet. When you put the list away, that copy is the one that gets moved.
   - **In the catalog**: every printing in the game's catalog, including ones you don't own. Click one, set the **Qty** and **Foil**, then click **Add to list**.
4. The dialog stays open so you can add several cards in a row. The title shows how many you've added so far. Click **Close** when you're done.

![The Add card to list dialog with results from your collection and the catalog](lists-add-card-dialog.png)

## Read a list

The open list splits its cards into two grids:

- **Owned**: the copies your collection already covers.
- **To buy**: the copies you still need.

A card you partly own shows up in both grids. For example, if the list needs 4 and you own 1, it shows 1 under **Owned** and 3 under **To buy**. Each grid's heading shows how many copies it holds. Click a heading to collapse or expand that grid. OmniCard remembers your choice in this browser. The **Export** button on a heading exports just that grid. See [Export a list](#export-a-list).

Each grid has these columns:

| Column | What it shows |
|---|---|
| (icon) | Whether you own the card. See the icons below. |
| **Card** | The card name. ✦ means foil. Hover over the name to see the card image. |
| **Set** | The set code and collector number. |
| **Owned** / **To buy** | How many copies of this card are in this grid. |
| **List qty** | How many the list needs in total. Type a new number to change it. |
| **Price** | The current market price for one copy. A dash means no price is known. |

Click the delete icon at the end of a row to take that card off the list.

### Icons in the first column

- **Collection icon**: you own this printing. It's green when you own enough copies and orange when you own some but not all.
- **Swap icon**: a stand-in, meaning another printing from your collection is filling in for the card. See [Use other printings you own](#use-other-printings-you-own).
- **Shopping cart**: awaiting purchase. Your copies were already moved and the rest still need to be bought. See [Awaiting purchase](#put-cards-away).

In the **To buy** grid, a grey **+N** next to the count means you have N more copies, but they're in an ignored location or listed for sale, so the list doesn't count them. See [Ignored locations](#ignored-locations).

### What counts as owned

A copy counts as owned only when it's the same printing and the same finish (foil or not), in a site you can see. A copy of the same printing in another language also counts, unless the list is set to one language. Copies that are listed for sale, sitting in an ignored location, flagged missing, or traded away don't count.

### Value totals

Above the table, **Total market value** is the cost of every card on the list. **To buy** is the cost of only the copies you don't own. Click **Refresh prices** to get the latest market prices.

## Update a list from its URL

When a Moxfield or Archidekt deck changes, you can bring those changes into your list. Nothing changes until you approve it.

1. Open the list and click **Update from URL**. Hover over the button to see the address the list came from.
2. If the list remembers its deck address, OmniCard checks it right away. Otherwise, paste an address into **Moxfield / Archidekt deck URL** and click **Check for changes**.
3. Review the changes. The summary shows how many cards changed and how many didn't. Each change is marked:
   - **Added**: the card is new in the deck. If you already own copies, a note says *you own N*.
   - **Removed**: the card is no longer in the deck.
   - **Quantity**: the deck has a different count, shown as old → new.
4. Tick the changes you want. All changes start ticked except removals of cards marked **(not from the URL)**. Those are cards you added by hand or stand-ins, so OmniCard leaves them unticked to keep them.
5. Click **Apply N changes**.

If the list already matches the deck, you'll see **The list already matches the deck.**

![The Update from URL dialog listing added, removed, and quantity changes](lists-update-from-url.png)

## Use other printings you own

Sometimes you don't own the exact printing on the list, but you do own the same card from another set. **Find in collection** lets those copies stand in.

1. Open the list and click **Find in collection**.
2. For each card you're short on, the dialog shows how many you **need**. Below that are the other printings you own, with their condition, location (including page and slot for binders), and how many are **Available**.
3. Each **Use** box starts at a suggested amount. Change the amounts as you like. You can't use more copies than a card needs, or use the same copies for two cards. If you try, OmniCard highlights the problem.
4. Click **Use N copies**.

The copies you chose become stand-ins on the list and get the swap icon. When you put the list away, those exact copies are moved.

Greyed-out copies are in ignored locations or listed for sale, so you can't use them. The tag on each one says why: **Ignored location** or **Listed for sale**. To change which locations are ignored, click **Ignored locations…** at the bottom of the dialog.

If the list has a card language set, only copies in that language are offered.

![The Find in collection dialog offering other printings as stand-ins](lists-find-in-collection.png)

## Set the card language

Use **Card language** at the top right of an open list to make the list language-specific, for example a Japanese-only deck.

- **Any language** (the default): copies in any language count as owned. Imported and new cards use the English printing.
- A specific language: only copies in that language count as owned. Imported cards, the buy list, and new cards all use that language's printing.

You can also choose the language when you import a list from a URL.

When you change the language, OmniCard switches every card on the list to that language's printing of the same set and number. A card that has no printing in that language keeps its English printing and gets a tag such as **No Japanese printing**. Hover over the tag for details. Cards you added from your collection, and stand-ins, keep the exact copy you chose.

> [!TIP]
> If a list imported before this worked shows printings in the wrong language, click **Refresh prices**. It also switches the cards to the list's language.

## Ignored locations

Some cards shouldn't be pulled into a list, like your sales binder or a deck you're playing. Ignore their locations, and lists won't count those cards or take them.

1. In an open list, click **Ignored locations…** in the **Put cards away** panel. You'll also find it in **Find in collection** and on the [Locations](/locations) page.
2. Tick each location to ignore. Locations are grouped by type, and by site if you have more than one site. Tick a group heading to ignore the whole group, or a site heading (marked *whole site*) to ignore everything at that site.
3. Use **Filter locations** to find a location by name.
4. Click **Save N changes**.

Locations in a site you can only read have a lock icon and can't be changed. Ignored locations also apply to decklist checks. For more about locations, see [Locations](help:locations).

## Print a list

Click **Print** in an open list and choose:

- **Print list**: every card on the list.
- **Print pick list**: the copies you own, grouped by where they are, so you can walk from location to location and pull them.
- **Print buy list**: only the copies you still need to buy.

Each one downloads as a PDF.

## Export a list

Export a list as text to paste into Moxfield or Archidekt, or as a CSV for a spreadsheet.

1. Click **Export** above the list. You can also click **Export** on the **Owned** or **To buy** heading to start with that part.
2. Choose what to export: **All cards**, **To buy**, or **Owned**.
3. Choose **Text** or **CSV**. A preview shows exactly what you'll get.
4. Click **Copy** to put it on the clipboard, or **Download** to save it as a file.

Text has one line per card, like this:

`1x Aragorn, the Uniter (LTR) 192`

That's the quantity, the card name, the set code in brackets, and the collector number. Foils end in `*F*`, and etched foils in `*E*`. Copies of the same printing and finish are combined into one line.

The CSV has the columns **Qty**, **Card Name**, **Set**, **Collector Number**, and **Foil**.

## Put cards away

When you've collected the cards, the **Put cards away** panel moves everything into place in one step.

1. **Move N owned cards to**: click **Choose location…** and pick where your owned copies go, such as the deck box.
2. **Add N new cards to**: click **Choose location…** and pick where your newly bought copies go. Set **Cond** for the new cards (NM by default).
3. When you pick a location for one row, the other row uses it too, unless you've already set it. Usually both go to the same place.
4. Click one of these:
   - **Move owned**: moves only the copies you own.
   - **Add new**: adds only the missing copies as new cards in your collection.
   - **Move & add**: does both.

Here's what happens:

- Owned copies move to the chosen location. If a copy is part of a larger stack, OmniCard splits off just the copies it needs.
- New cards are added in the list's card language, or in English when the list allows any language.
- Cards that are done come off the list. When every card is done, the list is deleted and a message confirms it.

In the location picker, you can't choose a deck box that's set to a different game. For more about choosing and creating locations, see [Locations](help:locations) and [Deck boxes](help:deck-boxes).

![The Put cards away panel with locations chosen for owned and new cards](lists-put-cards-away.png)

### Awaiting purchase

If you click **Move owned** before you've bought the rest, the cards you moved come off the list. The cards that still need buying stay on the list with a shopping-cart icon. This means *your copies were already moved, the rest is to buy*. Those cards won't count other copies in your collection as owned, so the same copies aren't counted twice. When you've bought the cards, use **Add new** to add them.

## Tips

- Lists also work as shopping lists. Import a deck, click **Print buy list**, and take the PDF to a card shop. To order online, export **To buy** as text and paste it into the store's mass-entry box.
- Before you put a list away, ignore locations you never want to pull from, like a sales binder.
- If a card shows as not owned but you're sure you have it, check its printing, foil, and language. Then try **Find in collection**.
- To check a decklist against your collection without saving it, use **Check decklist** on the Collection page. See [Collection](help:collection).
- To add a whole deck straight into a location as owned cards, see [Importing](help:importing).
- For decks you've already built, see [Deck boxes](help:deck-boxes).
