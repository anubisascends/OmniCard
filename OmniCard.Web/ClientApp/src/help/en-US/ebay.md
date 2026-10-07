# eBay

Connect OmniCard to your eBay seller account and publish cards from your collection as live eBay listings, without retyping anything on eBay.

## What the eBay integration does

With eBay connected, you can:

- Publish a card from your collection to eBay from the **List for sale** dialog, with a suggested title, description, condition and category.
- See which listings are live on eBay, open them, and update their price and details from the **Listings** tab on the [Sales](help:sales) page.
- End eBay listings automatically when you unlist a card or ship an order that includes it.

OmniCard connects to **one eBay seller account** for your whole OmniCard site. Everyone who lists on eBay publishes under that account.

Before you can list on eBay, an administrator sets things up once:

1. [Connect your eBay account](help:ebay#connect-your-ebay-account).
2. [Enter your seller settings](help:ebay#enter-your-seller-settings).
3. [Run seller setup](help:ebay#run-seller-setup).

> [!NOTE]
> The eBay options are on the **Administration** page, **eBay** tab. Some messages call this **Settings ▸ eBay**. If you can't see the tab, or its buttons and fields are greyed out, ask an administrator for access.

## Connect your eBay account

1. Go to **Administration ▸ eBay**.
2. Check the **Status**:
   - **Connected**: you're all set.
   - **Not connected**: continue below.
   - **Not configured**: the OmniCard server isn't set up for eBay yet. The message lists what's missing. Ask whoever installed OmniCard to add the server's eBay app credentials, then reload the page.
3. Click **Connect to eBay**. You're taken to eBay.
4. Sign in to your eBay seller account and approve access for OmniCard.
5. eBay sends you back to OmniCard, which shows **Connected to eBay.**

![The eBay tab with the connection status and seller settings](ebay-settings.png)

If you see **eBay connection failed. Please try again.**, click **Connect to eBay** again and finish signing in on eBay.

To stop using eBay, click **Disconnect**. Your existing listings in OmniCard stay as they are.

## Enter your seller settings

eBay needs an address for where your items ship from, plus shipping and return policies. Enter them under **Seller settings** on the same tab.

### Inventory location address

- **Location name**: a label for this address, for example your store name.
- **Address line 1**, **City**, **Postal code** and **Country (ISO-2, e.g. US)** are required. Use a two-letter country code, such as `US`.
- **Address line 2**, **State / province** and **Phone** are optional.

Until the address is complete, a warning reminds you that eBay needs it.

### Shipping policy

- **Free shipping**: turn this on to offer free shipping, or off to charge a **Flat shipping cost**.
- **Handling time (days)**: how soon you ship after a sale.
- **Shipping service**: USPS Priority, USPS Ground Advantage, USPS First Class or USPS Media Mail.

### Return policy

- **Accept returns**: turn this on to accept returns.
- **Return window (days)**: how long buyers have to return an item.
- **Return shipping paid by**: **Buyer** or **Seller**.

When you're done, click **Save seller settings**. Then run seller setup, as described next.

## Run seller setup

Seller setup creates your shipping location and your shipping, payment and return policies on eBay, using the seller settings you saved.

1. Make sure the status is **Connected** and your seller settings are saved.
2. Click **Run seller setup**.
3. Wait for **Seller setup complete.** If you see **Seller setup failed.**, read the details after the message, fix the problem (often an incomplete address), and run it again.

The **Setup status** section shows the result:

| Item | What it tells you |
|---|---|
| **Inventory location created** | Whether eBay has your shipping location |
| **Business policies created** | How many of the three policies (shipping, payment, returns) exist, for example 3/3 |
| **Setup last completed** | When setup last finished successfully |

> [!TIP]
> Changed your address or shipping options? Click **Save seller settings**, then **Run seller setup** again so eBay gets the new details. It's safe to run more than once.

## List a card on eBay

1. Open a card's details, for example by clicking it on the [Collection](help:collection) page or in a location, and click **List for sale**. In a [binder](help:binders), you can also open a card's menu and choose **List for sale**.
2. If you own more than one copy, set the **Quantity**. Listing fewer than all of them splits those copies off into their own stack.
3. Set the **Price**. It starts at the card's market price.
4. Choose **eBay** as the **Channel**. An **eBay listing** section appears, already filled in from the card:
   - **Listing title**: built from the card's name, set, number, foil, language and condition. Up to 80 characters, and a counter shows how many you've used.
   - **Description**: a short summary of the card. Edit it freely.
   - **Condition**: NM, LP, MP, HP or DMG. It starts at the card's condition.
   - **Listing type**: **Fixed price** or **Auction**. For an auction, also choose the **Auction duration** (1, 3, 5, 7 or 10 days).
   - **eBay category**: the category for individual collectible cards is selected. **Default (trading card singles)** works too.
5. Optionally add a **Note**. It stays in OmniCard and isn't sent to eBay.
6. Click **List on eBay**.

![The List for sale dialog with the eBay listing section](ebay-list-for-sale.png)

When it works, the dialog closes. The card shows on the **Listings** tab with the **eBay** channel, plus a **View on eBay** button that opens the live listing. OmniCard uses the card's picture from the card catalog as the listing photo.

> [!NOTE]
> If eBay isn't connected, the dialog shows "Not connected to eBay. Connect and run seller setup in Settings ▸ eBay before listing." and **List on eBay** stays disabled.

### Listing another copy of a card that's already on eBay

eBay doesn't allow two identical listings from the same seller. If you list a copy of a card that already has a live eBay listing in the same condition, OmniCard adds it to that listing and raises the listing's quantity. It doesn't create a second listing. The existing listing keeps its current price.

## If the eBay listing fails

OmniCard always lists the card in OmniCard first. If eBay then rejects the listing, the dialog stays open with **Listed locally, but the eBay push failed:** followed by eBay's reason. Click **Close**.

The card now shows on the **Listings** tab with the **eBay** channel, but it isn't live on eBay. There's no **View on eBay** button for it. To try again:

1. Fix the cause. Common causes are an incomplete seller setup, a disconnected account, or a title eBay doesn't accept.
2. On the **Sales ▸ Listings** tab, click **Unlist** on the card.
3. List the card again with the **eBay** channel.

## Update a live eBay listing

1. On the **Sales ▸ Listings** tab, find the card and click **Update on eBay**.
2. In **Update eBay listing**, change the **Price**, **Listing title**, **Description**, **Condition**, **Listing type** or **eBay category**.
3. Click **Update on eBay**.

The change is published to eBay, and the price in OmniCard updates to match. If eBay rejects the change, you see **eBay update failed:** with the reason, and the live listing stays as it was.

## End an eBay listing

- **Unlist a card**: on the **Listings** tab, click **Unlist**. If it was the last copy on that eBay listing, the listing is ended on eBay. If other copies of the same card are still listed, the eBay listing's quantity goes down instead.
- **Sell a card through an order**: when an order containing the card moves to a shipped lane, OmniCard ends the card's eBay listing so it can't sell twice. See [Sales](help:sales#move-an-order-through-the-board).

> [!WARNING]
> Ending a listing on eBay can occasionally fail, for example if eBay is unavailable. After shipping an order, check that the item no longer shows as live on eBay, and end it there yourself if it does.

## What you can and can't list on eBay

- **Singles only.** You can publish individual cards from your collection. Sealed product on the [Inventory](help:inventory) page can't be listed on eBay from OmniCard.
- **One card at a time.** Publishing happens only from the **List for sale** dialog for a single card. Listing many cards at once with the **eBay** channel, or switching an existing listing's channel to **eBay** with **Edit**, only marks them for eBay in OmniCard. Nothing is published on eBay.
- **eBay orders aren't imported automatically.** When something sells on eBay, create the order on the [Sales](help:sales#create-an-order) page with the **eBay** channel and add the sold card. Moving that order to a shipped lane removes the card from your collection.

## Troubleshooting

- **The status says "Not configured".** The OmniCard server doesn't have eBay app credentials yet. This is set up on the server by whoever installed OmniCard, not on this page.
- **After clicking Connect to eBay, you see "eBay isn't configured on the server yet".** Same cause: the server's eBay setup is incomplete. Ask whoever installed OmniCard to finish it.
- **"Seller setup failed."** Check that the address has **Address line 1**, **City**, **Postal code** and a two-letter **Country**, click **Save seller settings**, then run setup again.
- **The eBay listing failed.** See [If the eBay listing fails](help:ebay#if-the-ebay-listing-fails).
- **There's no "Update on eBay" button.** The card isn't live on eBay. It may have failed to publish, or been listed in bulk.

For more help, see [Troubleshooting](help:troubleshooting).
