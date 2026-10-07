# Binders

The binder view shows a binder location the way it looks on your shelf: two facing pages of pockets, with each card in its own page and slot. Use it to flip through a binder, see what's for sale, and arrange cards into pockets.

## Open a binder

1. Go to [Locations](/locations).
2. In the **Binders** group, click the binder's name. Binders open straight into the binder view.

You can also open a binder's regular location page and click **Open binder view**. To get back to the card table, click the binder's name in the page path at the top (**Locations ▸ *binder name* ▸ Binder**).

Need a new binder? Create a location with the type **Binder**. See [Locations](help:locations#create-a-location).

![A binder open to a two-page spread](binders-spread-view.webp)

## Flip through the binder

The binder is shown one spread (two facing pages) at a time, just like opening a real binder:

- The first spread shows page 1 on its own, on the right.
- After that, each spread shows an even page on the left and the next odd page on the right (pages 2–3, 4–5, and so on).

To move around:

- Click **‹ Prev** or **Next ›** to turn one spread at a time.
- Click a tab in the page strip (labelled *1*, *2–3*, *4–5*…) to jump straight to that spread. On narrow screens the strip scrolls; use its arrows to see more tabs.

The heading shows which pages you're looking at and how many pages the binder has, for example *Pages 2-3 · 12 pages*.

## What the pockets show

Each pocket shows the card's artwork. Hover over a card to see its name, condition and whether it's foil. Foil cards have a moving rainbow sheen so you can spot them at a glance.

### Price and sale badges

Small badges on each card tell you about its value and sale status:

| Badge | Where | Meaning |
|---|---|---|
| Price | Bottom left | The card's current market price. |
| **Listed** (blue) | Top left | The card is listed for sale. |
| **Picked** (orange) | Top left | The card is listed and has been picked into your for-sale location. |
| Channel and price | Bottom right | Where it's listed (Manual, TCGplayer or eBay) and the listed price. |

See [Sales](help:sales) for how listing and picking work.

### Card backs in empty pockets

A binder sheet has two sides, and each pocket on the front backs onto a pocket on the back. When a pocket is empty but the pocket behind it on the other side of the sheet holds a card, OmniCard shows that game's card back on a striped background. Hover over it to see *Card on the reverse side of this sheet*.

This matches what you'd see through a clear pocket, and helps you recognize the page you're holding.

## See a card's details

Click any card to open its details. From there you can edit its condition, foil, quantity, purchase price, note and tags, list it for sale, add it to a trade, split a stack, move it to another location, or delete it. See [Collection](help:collection).

You can also right-click a card and choose **Card details** or **List for sale**.

## Arrange cards in edit mode

Click **Edit** at the top right to start arranging the binder. The button changes to **Done editing**; click it when you're finished. Changes save as you make them.

If you don't see **Edit**, the binder is in a site you can only view. If changes show an error, you may not have permission to edit binders. Either way, ask an administrator for access.

![Edit mode with the Unplaced cards panel and edit toolbar](binders-edit-mode.webp)

In edit mode you get:

- An edit toolbar with **Add page**, **Layout** and **Remove page** buttons.
- The **Unplaced cards** panel on the left, listing cards that are in this binder but not in a pocket yet.
- Dashed pocket outlines highlighting where you can drop cards.

### The Unplaced cards panel

Cards end up in the Unplaced pool when you move or import them into a binder without choosing a pocket, when you split a stack, or when you remove a page.

- The heading shows how many unplaced cards there are, for example *Unplaced cards (14)*.
- Each entry shows the card's set, number, condition, foil, market price and sale status.
- Use the search box to filter the list. It accepts the full search syntax, such as `set:mh3`, `t:dragon` or `tag:trade`. See [Search syntax](help:search-syntax).
- Click an entry to open its details, or right-click it for **Card details** and **List for sale**.

When everything is placed, the panel says *Everything in this binder is placed.*

### Place, move and remove cards

- **Place a card**: drag it from the Unplaced cards panel onto a pocket.
- **Move a card**: drag it from one pocket to another, on the same spread.
- **Swap two cards**: drag a placed card onto an occupied pocket. The two cards trade places.
- **Replace a card from the pool**: drag an unplaced card onto an occupied pocket. The card that was there goes back to the Unplaced pool.
- **Remove a card from its pocket**: drag it from the pocket onto the Unplaced cards panel. The card stays in the binder; it just no longer has a pocket.

> [!TIP]
> To move a card to a pocket on a different spread, drag it to the Unplaced cards panel first, turn to the spread you want, then drag it into the pocket.

## Add or replace a card in a pocket

You can fill a specific pocket with a card from anywhere in your collection, or with a brand-new card from the catalog.

1. Right-click a pocket and choose **Add card to this pocket…** (or **Replace card in this pocket…** if it already holds a card).
2. The dialog shows the page and slot, and which card will be replaced.
3. Choose the **Game**, then search by **Name**, and optionally **Set** and **Collector #**.
4. Pick a result:
   - **In your collection** lists copies you own in other locations, with the location each is in. Click one to move a single copy into the pocket right away. If it's part of a stack, only one copy moves.
   - **In the catalog** lists every matching printing. Click one, set **Condition**, **Purchase price** and **Foil**, then click **Add to pocket** to add a new card to your collection in this pocket.

![The Add card to pocket dialog](binders-add-to-pocket.png)

If the pocket already held a card, that card goes back to the Unplaced pool.

> [!NOTE]
> **In your collection** only lists copies in other locations. To place a copy that's already in this binder, drag it from the Unplaced cards panel.

## Pages and layout

### Add pages

In edit mode, click **Add page** and choose:

- **Double-sided sheet**: adds a sheet with a front and a back (two pages).
- **Single-sided page**: adds a sheet with one usable side (one page).

New sheets are added at the end of the binder.

### Change the layout

Use **Layout** to set how many pockets each page has. The choice applies to the whole binder.

| Layout | Pockets per page |
|---|---|
| 2 × 2 | 4 |
| 3 × 3 | 9 |
| 3 × 4 | 12 |
| 4 × 4 | 16 |

Most trading-card binder pages are 3 × 3.

### Remove a page

In edit mode, the toolbar shows a **Remove page** button for each page on the current spread (for example **Remove page 4** and **Remove page 5**).

Removing a page removes the whole physical sheet it's on. For a double-sided sheet, that's both sides. Cards on the removed sheet go back to the Unplaced pool, and the pages after it move up to close the gap. A binder must keep at least one page.

> [!WARNING]
> **Remove page** works immediately, without asking you to confirm. Check which sheet you're on before you click it.

## Troubleshooting

- **There's no Edit button.** The binder is in a site you can only view. Ask an administrator for write access.
- **Editing shows an error.** You may not have permission to edit binders. Ask an administrator.
- **A card I added isn't showing in a pocket.** It's in the Unplaced pool. Click **Edit** and look in the **Unplaced cards** panel.
- **A card I'm looking for isn't in the Unplaced panel.** Clear the panel's search box. If it still isn't there, it may be in another location; use **Add card to this pocket…** and look under **In your collection**.
