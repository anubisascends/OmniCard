# Dashboard

The Dashboard shows what your whole collection cost, what it's worth today, and how much profit your sales have made. It opens when you first sign in. Everything on it is a **widget** you can move, resize, change or remove, and you can add your own: totals, breakdowns, card searches, charts, SQL queries and notes.

## Open the Dashboard

Click **Dashboard** at the top of the sidebar, or go to [the Dashboard](/). If you don't see **Dashboard** in the sidebar, ask an administrator for access.

![The Dashboard with the five totals and the By Game and By Category tables](dashboard-overview.png)

Until you or an administrator change it, the Dashboard shows five totals over two breakdown tables. The chip beside the title says which layout you're looking at: **Your own layout** once you've saved your own, otherwise the administrator's **Default layout** or the **Built-in layout**. Click the refresh button at the top right to reload every widget.

> [!NOTE]
> Widgets never follow the game picked in the top bar. A card search widget picks its own game, or **All games**.

## The totals

The row of tiles at the top sums up everything you own.

| Tile | What it shows |
|---|---|
| **Total Units** | How many items you own: every single card plus every sealed item, counting each copy |
| **Cost** | What you paid, based on the purchase price recorded for each card or item |
| **Market** | What everything is worth at today's market prices |
| **Unrealized** | **Market** minus **Cost**: the gain or loss you'd have if you sold everything today |
| **Realized Profit** | Profit from items you've actually sold: what buyers paid, minus what those items cost you, minus marketplace fees |

**Unrealized** and **Realized Profit** are green when they're positive and red when they're negative.

## The breakdown tables

Below the totals, two tables split the same numbers into groups. Each row shows **Units**, **Cost** and **Market** for its **Group**.

- **By Game** has one row per game you own cards or product for.
- **By Category** splits your holdings by kind of product: single cards, and sealed product such as cases, boxes, packs, decks and bundles.

## Customize your dashboard

Click **Customize** to change the Dashboard. Each widget gets a dashed outline and a small toolbar:

- **Move** a widget by dragging its handle (the dotted grip). The others make room.
- **Resize** it by dragging its bottom-right corner.
- **Edit** it with the pencil.
- Open the **More actions** menu (⋮) to **Duplicate** or **Remove** it.

Click **Add widget** to add one, then **Save** to keep your changes, or **Cancel** to throw them away. Your saved dashboard is yours alone: other people keep seeing theirs.

![Customizing the Dashboard: each widget has move, edit and more-actions buttons](dashboard-customize.png)

To go back to the dashboard everyone else sees, click **Customize**, then **Use the default dashboard**. This deletes your own layout.

> [!TIP]
> On a phone, widgets are shown one under another and can't be dragged or resized. You can still edit, duplicate and remove them, but arrange them on a larger screen.

## Add a widget

**Add widget** opens a list of starting points. Pick one, adjust it in the editor, then click **Add**. The new widget goes at the bottom of the Dashboard.

![The Add a widget list: totals, breakdowns, card searches, SQL and notes](dashboard-add-widget.png)

| Widget | What it shows |
|---|---|
| **Total** | One number. Pick it from **Holdings** (**Total Units**, **Cost**, **Market**, **Unrealized**) or **Sales** (**Realized Profit**, **Sales Proceeds**, **Cost of Sold Items**, **Marketplace Fees**, **Items Sold**, **Shipping Cost**, **Shipping Charged**). |
| **Breakdown table** / **Breakdown chart** | **Holdings by game**, **Holdings by category** or **Sales by game**. |
| **Search totals** | How many of your cards match a search, and what they cost and are worth. |
| **Most valuable cards** | The top cards matching a search. |
| **Cards by set** | Cards matching a search, totaled per group. |
| **SQL query** | Your own query over the database (see [SQL widgets](#sql-widgets)). **Orders per month** and **Cards added per month** are ready-made examples. |
| **Note** | Your own text, such as a heading, a reminder or links. |

Card search widgets need permission to view the Collection, and SQL widgets need **Write SQL widgets**. You only see the starting points you can use.

## Edit a widget

The editor has the widget's settings on the left and a live **Preview** on the right.

![Editing a Cards by set widget, with a bar chart preview](dashboard-widget-editor.png)

- **Title**: leave it empty for an automatic title that follows the settings.
- **Data**: what the widget shows. This depends on the kind of widget (see below).
- **Show as**: **Number**, **Table** or **Chart**. **Total** widgets are always a number.
  - **Number** shows one value. Pick which with **Value**. Turn on **Green when positive, red when negative** for gains and losses.
  - **Table** shows rows. **Columns** picks which columns appear, and in what order. Leave it on **All** to show every column.
  - **Chart** draws **Columns**, **Bars**, **Line**, **Area** or **Pie**. **Labels** picks the column that names each point, and **Values** the columns that are plotted. **Automatic** usually picks the market value. A pie chart uses the first value and folds anything past its seventh slice into **Other**.
- **Column formats** changes how a column is shown: **Number**, **Money**, **Percent**, **Text** or **Date**. This is mostly useful for SQL results.

Click **Apply** to put the change on the Dashboard, then **Save**.

### Card search widgets

- **Game**: one game, or **All games**.
- **Search**: the same search syntax as the [Collection](help:collection) page, such as `r>=rare` or `set:dmu`. Leave it empty for every card. See [Search syntax](help:search-syntax).
- **Show**:
  - **Totals**: cards, units, cost and market value.
  - **Top cards**: sorted by **Total value**, **Card price**, **Quantity**, **Name** or **Newest**.
  - **Grouped totals**: per **Set**, **Rarity**, **Condition**, **Language**, **Foil**, **Game**, **Location** or **Listing status**. Sort them by **Total value**, **Units**, **Cards** or **Group order**.
- **Rows**: how many cards or groups to list, up to 100. When there are more, the widget says **Showing the first … rows**.

Card searches only count cards in sites you can see, and leave out cards you've traded away.

## SQL widgets

A SQL widget runs your own query and shows the result as a number, a table or a chart. You need the **Write SQL widgets** permission (Administration ▸ Users or Roles, under **Dashboard**). Administrators have it.

- **Database**: **OmniCard** (your collection, sales and inventory) or one game's card catalog.
- **SQL**: one `SELECT` query. Click **Run** (or press **Ctrl+Enter**) to preview it.
- **Tables** lists every table you can query and its columns. Click a name to insert it.

Queries can only read. Anything else is refused before it runs, including changing data, running procedures, several statements at once, or reading other databases. The **Users** table, which holds sign-in secrets, can't be read. A query stops after 30 seconds and returns at most 1,000 rows.

> [!TIP]
> Name your columns with `AS`. The names become the table headings and chart labels, for example `SELECT FORMAT(OrderDate, 'yyyy-MM') AS Month, COUNT(*) AS Orders FROM Orders GROUP BY FORMAT(OrderDate, 'yyyy-MM') ORDER BY Month`.

Without **Write SQL widgets**, you still see the SQL widgets on the default dashboard. You can keep them in your own dashboard and move, resize or retitle them, but you can't change their queries. A copied SQL widget stops working if the administrator removes it from the default dashboard.

## The default dashboard (administrators)

Administrators set the dashboard that everyone without their own sees. Click **Customize**, switch from **My dashboard** to **Everyone's default**, make your changes, then click **Save**.

**Restore the built-in dashboard** removes everyone's default, so those users see the five totals and two tables again.

## How the numbers are worked out

- **Totals and breakdowns count everything**: all games, all sites and all locations. Choosing a game in the top bar doesn't change them.
- **Traded-away cards are left out** once a trade is finalized. See [Trades](help:trades).
- **Single cards** are valued at their current market price times their quantity. Foil and non-foil copies use their own prices.
- **Sealed product** is valued at the **Market** price shown for it on the Inventory page. Product with no market price counts as zero. See [Inventory](help:inventory).
- **Missing purchase prices count as zero.** If you didn't enter what you paid, **Cost** is understated and **Unrealized** looks bigger than it really is. You can add purchase prices when you scan, or edit them later in the [Collection](help:collection).
- **Missing market prices count as zero.** Some cards have no price from their game's price source, so **Market** may be a little low. See [Troubleshooting](help:troubleshooting).
- **Realized Profit** is based on the cards and items you've sold. When only part of a stack was sold, only the cost of the sold copies is counted. Marketplace fees come from shipped and completed orders. See [Sales](help:sales).
- Totals and breakdowns can be up to 30 seconds old.

> [!TIP]
> Market prices are only as fresh as the last price update. An administrator can refresh them under **Administration ▸ Catalog Data** with **Update prices**, or have them refreshed every day with **Automatic updates**. See [Administration](help:administration#automatic-updates).

## Related topics

- [Collection](help:collection): edit purchase prices and see each card's market price
- [Search syntax](help:search-syntax): what you can type in a card search widget
- [Locations](help:locations): the value of each binder, box or deck
- [Sales](help:sales): orders and listings that feed **Realized Profit**
- [Inventory](help:inventory): sealed product and its market prices
