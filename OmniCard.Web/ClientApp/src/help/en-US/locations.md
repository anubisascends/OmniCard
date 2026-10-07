# Locations

Locations are the binders, boxes, deck boxes and display cases where your cards physically live. This topic explains sites, the Locations page, and how to view, add, import and move cards in a single location.

## How cards are organized

OmniCard mirrors the way you store cards in real life, in three levels:

| Level | What it is | Examples |
|---|---|---|
| Site | A major physical place | Your home, your shop, a storage unit |
| Location | A container inside a site | A binder, a box, a deck box, a display case |
| Card | An owned copy (or stack of copies) in a location | 3× Lightning Bolt, NM |

Every card you own sits in exactly one location, and every location belongs to exactly one site.

### Sites

Most people only ever use one site, called **Default**, and can ignore sites entirely. Sites matter when several people share one OmniCard collection, for example a home collection and a shop.

- The **Default** site always exists, is visible to everyone, and holds **Bulk** plus any location not assigned to another site.
- Other sites are only visible to the people an administrator has allowed to see them.
- Your access to a site is either **Read** (you can browse and search its locations and cards) or **Write** (you can also change them).
- A location in a site you can only read shows a lock icon and the words *view only*. You can open it and browse its cards, but buttons that change things are hidden.

> [!NOTE]
> Administrators create sites and decide who can see them under **Administration ▸ Sites**. See [Administration](help:administration). If you expect to see a site and don't, ask an administrator for access.

### Location types

When you create a location you choose its type. The type controls how the location looks and what it can do.

| Type | Use it for |
|---|---|
| **Binder** | Pages of pockets. Opens in the visual binder view, where each card has a page and slot. See [Binders](help:binders). |
| **Box** | Long boxes, storage boxes, anything without a set order. |
| **Deck Box** | A built deck. Holds one game only and can check the deck against a format's rules. See [Deck boxes](help:deck-boxes). |
| **Display Case** | Showcase or shop display cards. |

**Bulk** is a built-in location that always exists in the Default site. You can't delete it. Cards from a deleted location can be sent there.

## The Locations page

Open [Locations](/locations) from the navigation menu to see every location you can access.

![The Locations page with grouped location tables](locations-page-overview.png)

At the top of the page you'll find:

- **Site**: choose **All Sites** or one specific site. Your choice is remembered in this browser.
- The add bar for creating a new location (see [Create a location](help:locations#create-a-location)).
- **Hide empty locations**: hides locations that have no cards for the selected game.
- **Ignored locations…**: opens the dialog that controls which locations lists never take cards from (see [Ignore a location for lists](help:locations#ignore-a-location-for-lists)).

Below that, locations are shown in groups:

- **Always Available** comes first, holding every location you've marked always available.
- Then one group per type (**Binders**, **Boxes**, **Bulk**, **Deck Boxes**, **Display Cases**), in alphabetical order.

Click a group heading to collapse or expand it. Collapsed groups stay collapsed the next time you visit.

### Columns

| Column | Meaning |
|---|---|
| **Name** | Click it to open the location. Binders open straight into the binder view. An **Ignored** chip means lists don't take cards from it. |
| **Site** | Shown when you're viewing **All Sites** and more than one site exists. A lock icon means view only. |
| **Type** | The location type. Deck boxes also show their deck type and game. |
| **Cards** | Total copies in the location. |
| **Unique** | Number of distinct printings. |
| **Market** | Current market value of the cards. |
| **Cost** | What you paid (from each card's purchase price). |
| **Δ** | Market value minus cost, in dollars and percent. Green is a gain, red a loss. |

Click any column header to sort by it.

### The game selector and locations

The game selected at the top of the app affects this page:

- **Cards**, **Unique**, **Market** and **Cost** count only cards from that game. Choose **All Games** to count everything.
- Deck boxes that belong to a different game are hidden.
- Always-available locations are always shown, whatever game is selected.

## Create a location

1. On the [Locations](/locations) page, type a name in **New location name**. Names must be unique. If the name is already used, you'll see *This name is already in use*.
2. Pick a **Type**.
3. If you picked **Deck Box**, choose a **Game** (required) and optionally a **Deck type**. See [Deck boxes](help:deck-boxes).
4. If you can write to more than one site, choose the site in **Create in site**. By default, new locations go into the site you're filtering by, or the Default site.
5. Click **Add**.

![Creating a new deck box from the add bar](locations-add-bar.png)

> [!TIP]
> You don't have to come to this page to make a new location. Most "move to location" pickers in OmniCard have a **New location** button that creates one and selects it in one step.

## Manage a location

Each row has a **⋮** menu on the right with these actions. If you see a lock icon instead, the location is in a site you can only view.

![The location actions menu](locations-row-menu.png)

### Rename

Choose **Rename…**, type the new name, and confirm.

### Change a deck box's game or deck type

For deck boxes, choose **Game & deck type…**. See [Deck boxes](help:deck-boxes#set-the-game-and-deck-type).

### Move to another site

Choose **Move to site…**, pick the **Destination site**, and click **Move**. Every card in the location moves with it. This option only appears when you can write to at least one other site.

### Always available

Choose **Set always-available** to pin a location to the top of the page and of every location picker, and to keep it visible no matter which game is selected. It's handy for a "to sort" box or a trade binder you use constantly. Choose **Unset always-available** to turn it off. Bulk can't be changed.

### Ignore a location for lists

Choose **Ignore for lists** when a location's cards should never be pulled for a list or counted as owned: a sales binder, a deck you're playing, or a whole site. Ignored locations show an **Ignored** chip. Choose **Stop ignoring for lists** to undo it.

To change many locations at once, click **Ignored locations…** above the tables:

1. Use **Filter locations** to narrow the list if needed.
2. Tick the locations to ignore. Locations are grouped by site and type; tick a group's box to tick everything in it, or tick a site's box (marked *whole site*) to ignore the entire site.
3. Click **Save**. The button shows how many changes you've made.

Ignored locations are also skipped by **Check decklist** on the Collection page. **Find in collection** on a list still shows those cards, marked as ignored. See [Lists](help:lists).

### Delete

1. Choose **Delete…** and confirm that you want to delete the location.
2. A second question asks *Move its cards to Bulk?*
   - Click **OK** to keep the cards and move them to Bulk.
   - Click **Cancel** to delete the cards along with the location.

> [!WARNING]
> Clicking **Cancel** on the second question permanently deletes every card in the location. If you want to keep the cards, click **OK**.

Bulk can't be deleted.

## Open a location

Click a location's name to open its page. (Binder names open the [binder view](help:binders); use the binder's name in the page path at the top to get to its card table.)

![A location's page showing its card table](locations-detail-page.png)

The location page shows:

- The location's name, its type, and its site. A site you can only view shows *view only* next to its name.
- **Open binder view**, for binders.
- **Audit**, **Import** and **Add card** buttons (when you're allowed to change this location).
- A summary line with the number of cards and their market value.
- For deck boxes, a panel with the game, deck type and legality warnings. See [Deck boxes](help:deck-boxes).
- A search box and the card list.

### Find cards in a location

Type in the search box to filter the cards. Plain text matches card names, and you can use the full search syntax, such as `t:creature` or `set:mh3`. See [Search syntax](help:search-syntax).

### Table view and stacked view

Use the two buttons to the right of the search box to switch views. OmniCard remembers your choice.

- **Table view** (list icon): a sortable table with name, set, number, rarity, condition, language, foil, quantity, market price and sale status. Turn on **Stack duplicates** to combine identical copies into one row.
- **Stacked view** (columns icon): cards drawn as overlapping stacks grouped by type or tag, like a deck-building site. It works for any location but is most useful for decks. See [Deck boxes](help:deck-boxes#stacked-view).

Click any card to open its details, where you can edit condition, foil, quantity, purchase price, note and tags, list it for sale, add it to a trade, split a stack, move it, or delete it. See [Collection](help:collection).

## Add cards to a location

1. Click **Add card**.
2. Choose the **Game** (a deck box is locked to its own game).
3. Search by **Name**, and optionally narrow with **Set** and **Collector #**.
4. Click the printing you want.
5. Set **Condition**, **Quantity**, **Purchase price** and **Foil**, then click **Add card**.
6. The dialog stays open so you can add more. Click **Done** when you're finished.

To add many cards at once, scan them (see [Scanning](help:scanning)) or import a file.

## Import into a location

The location's **Import** button adds a whole file or deck to this location in one go. It's stricter than the main [Import](help:importing) page: there's no location picker, and it's all or nothing.

1. On the location page, click **Import**.
2. Choose a source:
   - **CSV file**: click **Choose CSV file** and pick an OmniCard, TCGplayer, Moxfield or ManaBox collection CSV.
   - **Deck URL**: paste a public Moxfield or Archidekt deck link (Magic: The Gathering) and pick the **Condition** to give the cards.
3. Click **Import**.

![The Import into location dialog](locations-import-dialog.png)

Every card must match a card in the catalog. If any line has a problem, **nothing is imported**. Instead, OmniCard lists each problem with its row number and card name so you can fix the file and try again.

When the import succeeds, you'll see how many cards were added. If a line's exact printing isn't in the catalog but the card is, OmniCard uses another printing of the same card and lists those substitutions so you can check them.

> [!TIP]
> Need to import into several locations, skip duplicates, or import a decklist file? Use the [Import](/import) page instead. See [Importing](help:importing).

## Audit a location

Click **Audit** to check what's really in a location against what OmniCard thinks is there. You scan the location's cards, review the differences, and commit to fix the records. When you finish, you return to the location page and see a summary of what changed. See [Location audit](help:location-audit).

## Move cards between locations

There are several ways to move cards:

- **Several cards from a location**: in **Table view**, click **Select**, tick the cards, then click **Move to…** and pick the destination. Click **Done** to leave selection mode.
- **One card**: click the card to open its details, click **Change** next to **Location**, and pick the destination.
- **A whole location to another site**: use **Move to site…** in the Locations page menu.
- **From the collection**: the same **Select ▸ Move to…** tools work on the [Collection](/collection) page. See [Collection](help:collection).

The location picker groups locations by type, with **Always Available** first. Type in **Search locations…** to filter, or click **New location** to create one on the spot with **Create & select**. Deck boxes for a different game appear greyed out (*This deck box only holds … cards*), and locations in sites you can only view aren't listed.

In selection mode you can also use **Bulk edit**, **List for sale**, **Export CSV** and **Delete** on the selected cards.

## Troubleshooting

- **A location is missing.** Check the **Site** filter (try **All Sites**), turn off **Hide empty locations**, and check the game selector. Deck boxes for other games are hidden. If it's in a site you haven't been given access to, ask an administrator.
- **I can't change a location.** A lock icon or *view only* means you only have read access to its site. Ask an administrator for write access.
- **Audit or Import buttons are missing.** These need extra permissions. Ask an administrator.
- **Import says nothing was imported.** That's expected when any line has a problem. Fix every listed row and import again.
- **I can't move a card into a deck box.** The deck box holds a different game. See [Deck boxes](help:deck-boxes#one-game-per-deck-box).
