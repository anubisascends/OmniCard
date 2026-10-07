# Sales

Sell cards from your collection: list them for sale, pull them for shipping, track orders from creation to completion, and keep a customer list.

## How selling works in OmniCard

Selling usually follows these steps:

1. **List** a card for sale from your collection, setting a price and a sales channel (Manual, TCGplayer or eBay).
2. **Pick** it: pull the card from where it's stored, and optionally move it to your for-sale location.
3. **Record the order** for the buyer and add the cards they bought.
4. **Move the order across the board** as you pack, ship and complete it. When it reaches a shipped lane, OmniCard records the sale and removes the sold cards from your collection.

The [Sales](/sales) page has three tabs: **Orders**, **Listings** and **Customers**.

![The Sales page Orders board](sales-orders-board.png)

> [!NOTE]
> If you don't see **Sales** in the menu, or some buttons are missing, ask an administrator for access.

## List cards for sale

You list cards from wherever you view them, not from the Sales page.

### List one card

1. Open a card's details. For example, click a card on the [Collection](help:collection) page or in a location, or open a card's menu in a [binder](help:binders) and choose **Card details**.
2. Click **List for sale**.
3. If you own more than one copy, set **Quantity (you have N)**. Listing fewer than all of them splits those copies off into their own stack, so only the listed copies get picked later. Listing all of them lists the whole stack.
4. Check the **Price**. It starts at the card's market price.
5. Choose a **Channel**: **Manual**, **TCGplayer** or **eBay**.
6. Optionally add a **Note**.
7. Click **List for sale**.

![The List for sale dialog](sales-list-for-sale.png)

Choosing **eBay** adds an eBay section and publishes the listing on eBay. See [eBay](help:ebay).

> [!TIP]
> In a binder, you can also open a card's menu and choose **List for sale** directly.

### List many cards at once

1. On the Collection page or a location page, click **Select** and tick the cards you want.
2. Click **List for sale** in the selection bar.
3. Choose a **Channel** and optionally add a **Note**, then click **List for sale**.

Each selected card is listed as a whole stack at its current market price. Cards that are already listed are skipped. Fine-tune prices afterwards on the **Listings** tab.

> [!NOTE]
> Bulk listing with the **eBay** channel only marks the cards as listed in OmniCard. It doesn't publish anything on eBay. To publish on eBay, list each card on its own. See [eBay](help:ebay).

A card that's already listed shows **Already listed for sale** in its details. Unlist it from the **Listings** tab before listing it again.

## Manage listings

The **Listings** tab shows every active listing.

![The Listings tab](sales-listings.png)

| Column | Meaning |
|---|---|
| **Name** | The card name (✦ marks a foil) |
| **Set** | The set code |
| **Cond** | The card's condition |
| **Channel** | Manual, TCGplayer or eBay |
| **Qty** | How many copies are listed |
| **Price** | The listed price per copy |
| **Status** | **Listed** (waiting to be pulled) or **Picked** (pulled for sale) |

The buttons at the end of each row:

- **Mark picked (move to sales location)**: the check mark, shown on listings that are still **Listed**. See [Pick listed cards](help:sales#pick-listed-cards).
- **Edit**: change the listing's price, channel, quantity or note.
- **View on eBay**: opens the live eBay listing in a new tab. Shown only for cards published on eBay.
- **Update on eBay**: changes the live eBay listing. Shown only for cards published on eBay. See [eBay](help:ebay).
- **Unlist**: takes the card off the market. The card stays in your collection.

### Edit a listing

1. Click the **Edit** button on the listing's row.
2. In **Edit listing**, change the **Price**, **Channel**, **Quantity** or **Note**.
3. Click **Save**.

> [!NOTE]
> Editing a listing changes it in OmniCard only. To change a card that's live on eBay, use **Update on eBay**.

## Pick listed cards

Picking means pulling listed cards from storage so they're ready to sell. When you mark a listing picked, its status changes to **Picked**. If your for-sale location is turned on, the card also moves to that location, so OmniCard always knows where it is.

1. On the **Listings** tab, click **Pick list (PDF)** to download a printable list of the cards to pull and where each one is stored. The list follows the game selector at the top of the app.
2. Pull the cards.
3. Click the check mark on each listing you pulled, or click **Mark all picked (N)** to mark every listed card at once.

If picking fails because no for-sale location is set, an error appears with a link to **Settings**. Set one up as described below.

### Set up the for-sale location

These options are on the **Administration** page, **Sales** tab:

- **For-sale location**: the location picked cards move to. Click **Change** to pick one, or **Clear** to remove it.
- **Move picked cards to the for-sale location**: when on, marking a listing picked moves the card to the for-sale location. When off, picking only changes the status and the card stays where it is.

![The Sales tab on the Administration page](sales-settings-for-sale-location.png)

> [!TIP]
> Create a location called "For Sale" on the [Locations](help:locations) page and choose it here. Your pulled cards then stay together in one place until they ship.

## Work with orders

The **Orders** tab is a board. Each column, or lane, is a stage of the order, and each order is a card in a lane. A card shows the customer, the channel, the order number, the item count and the order total.

The standard lanes are:

| Lane | What it means |
|---|---|
| **Created** | A new order. You can still edit everything. |
| **Packed** | Packed and ready to ship. You can still edit everything. |
| **Shipped** | Sent. OmniCard records the sale and removes the sold cards from your collection. |
| **Completed** | Finished. |
| **Cancelled** | Called off. Nothing is removed from your collection. |

Each lane's dot and the colored stripe on its order cards help you scan the board at a glance. The number next to a lane's name is how many orders it holds.

> [!NOTE]
> Your board's lanes can be customized for your store, so lane names and colors may differ. Each lane still behaves like one of the stages above.

### Create an order

1. Click **New order**.
2. Choose a **Customer**. If the buyer isn't listed yet, add them on the **Customers** tab first.
3. Choose the **Channel** and optionally enter an **Order # (optional)**, such as a marketplace order number.
4. Click **Create**. The order's details panel opens so you can add cards.

### Add cards to an order

1. In the order's details panel, type at least two letters of a card name in **Add a card (search your collection)**. Results come from your collection and follow the game selector at the top of the app.
2. Each result shows the card's name, set and condition, plus a price box that starts at the market price. Change the price if needed.
3. Click **+** to add the card. Each click adds one copy.

The **Items** heading shows the number of items and the order total. To remove a card, click the trash button on its line.

![An order's details panel](sales-order-detail.png)

> [!TIP]
> Building a big order? Leave the search text in place and click **+** on each result you need. The list stays open, so you can add several cards in a row.

### Fill in order details

The top of the details panel holds the order's details: **Channel**, **Order #**, **Tracking**, **Ship charged** (what the buyer paid for shipping), **Ship cost** (what shipping cost you), **Fees** (marketplace fees) and **Notes**. Click **Save header** to keep your changes.

### Move an order through the board

Drag an order's card and drop it on another lane. The move saves right away.

When an order enters a shipped lane for the first time:

- The sale is recorded, and the sold copies are removed from your collection.
- The cards' listings are marked sold.
- Any live eBay listing for those cards is ended, so they can't sell twice.

> [!WARNING]
> Add every card to the order before you move it to **Shipped**. Cards added afterwards aren't removed from your collection automatically.

### Edit a shipped or completed order

Once an order is past **Packed**, its details panel is locked. A message says that header and line edits are locked until you move the order back to Created or Packed.

To correct a mistake, drag the order back to **Created** or **Packed**, make your changes, then move it forward again.

> [!WARNING]
> Moving a shipped order back doesn't return its cards to your collection, and moving it to **Shipped** again records the sale again. Only do this to fix order details, and check your collection afterwards.

### Delete an order

While an order is in **Created** or **Packed**, open it and click **Delete order**, then confirm. Shipped and completed orders can't be deleted. Move them to **Cancelled** instead if they didn't go through.

## Print a receipt

1. Click an order on the board to open its details.
2. Click **Print receipt**. A print-ready receipt opens in a new tab, sized for your receipt printer, and your browser's print dialog appears.
3. Or click **PDF** to download the receipt as a PDF file.

An administrator sets the store name, address, logo, paper width, font size, footer text and whether prices are shown. These options are on the **Administration** page, **Receipts** tab. See [Administration](help:administration).

![A printed receipt preview](sales-receipt.png)

## Import orders from a CSV file

Bring in orders from a marketplace export, such as a TCGplayer shipping export, instead of typing them in.

1. On the **Orders** tab, click **Import CSV**.
2. **Choose file**: click **Choose CSV file…** and pick your file.
3. **Map columns**: tell OmniCard which column in your file holds each order field.
   - Pick a **Template**. **TCGPlayer Shipping Export (built-in)** is selected to start, and it already matches TCGplayer's columns.
   - Choose the **Channel** the orders came from.
   - For each field, pick the matching column from your file, or leave it **— not mapped —**.
   - **Order number** must be mapped. OmniCard uses it to recognize orders you've already imported.
4. Click **Preview**.
5. **Review & import**: check the rows. Untick any you don't want, then click **Import N orders**.

![The Import orders from CSV dialog, mapping step](sales-import-orders-map.png)

The fields you can map are **Full name**, **First name**, **Last name**, **Order number**, **Order date**, **Address line 1**, **Address line 2**, **City**, **State / province**, **Postal code**, **Country**, **Shipping fee paid**, **Item count**, **Value of products**, **Tracking number** and **Carrier**.

In the review step, the **Status** column tells you what will happen to each row:

| Status | Meaning |
|---|---|
| New customer · New order | Creates the order and a new customer |
| Matched customer · New order | Creates the order for an existing customer (matched by name and postal code), and updates that customer's address |
| Already imported | This order number already exists, so the row is skipped |

Warnings, such as a date that couldn't be read, appear above the list. Imported orders start in the **Created** lane.

> [!NOTE]
> Imported orders include the buyer, order number, date, shipping and tracking, but not the individual cards. Open an imported order and add cards from your collection if you want them removed from your collection when it ships.

### Save your own import template

If you import from a source whose columns don't match a template:

1. Map the columns as described above.
2. Enter a **New template name** and click **Save as template**.

Next time, pick your template from the **Template** list. To remove a template you saved, select it and click **Delete this template**. The built-in template can't be deleted.

## Manage customers

The **Customers** tab lists everyone you sell to, with their **Name**, **Email**, **Phone** and **Location** (city and state).

![The Customers tab](sales-customers.png)

- **Add a customer**: click **New customer**, fill in at least the **Name**, and click **Save**. You can also add email, phone and a full mailing address.
- **Edit a customer**: click the pencil button on their row.
- **Delete a customer**: click the trash button and confirm.

Customers are also created automatically when you import orders from a CSV file.

## Troubleshooting

- **"Mark picked" shows an error about the for-sale location.** No for-sale location is set, or the one you chose was deleted. Set one on the **Administration** page, **Sales** tab, or turn off **Move picked cards to the for-sale location**.
- **I can't edit an order.** It's past **Packed**. See [Edit a shipped or completed order](help:sales#edit-a-shipped-or-completed-order).
- **A card won't list.** It's already listed. Find it on the **Listings** tab and click **Unlist** first.
- **My search in "Add a card" finds nothing.** Type at least two letters, and check that the game selector at the top of the app is set to the card's game or **All Games**.

For more help, see [Troubleshooting](help:troubleshooting).
