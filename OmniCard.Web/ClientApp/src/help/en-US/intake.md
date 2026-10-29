# Intake (buying singles)

Buy cards from a seller at the counter. Scan their cards with your phone, check the matches, and OmniCard shows each card's market value with a cash offer and a trade (store credit) offer. When the seller agrees, the cards go into your intake location and any trade value is added to the customer's store credit.

## How intake works

1. Open [Intake](/intake). On a phone it's in the bottom bar.
2. Scan each of the seller's cards: tap **Take photo**, or use **Add images** or **Use webcam**. Scanning works exactly like the [Scan page](help:scanning).
3. Check every match. Correct any card OmniCard got wrong, set its condition, then confirm it.
4. Tap **Complete**. The quote opens.
5. Show the seller the offer. Tap **Customer view** to hand them the phone.
6. Tap **Buy**, choose the customer, and confirm.

Nothing is added to your collection until you tap **Buy**. Tap **Back to scanning** at any time to scan more cards or fix one; your scans stay where you left them.

## Read the quote

![A quote with one card paid in cash and one in trade](intake-quote.png)

The top of the quote shows:

- **Market value**: the total market value of the cards you're buying.
- **Cash**: what everything would pay in cash.
- **Trade**: what everything would pay in store credit.
- **Paying**: what you'll actually pay, adding up each card's choice of cash or trade.

When an administrator sets the same percentage for cash and trade, the quote shows a single **Offer** instead.

Each card shows its market (and low) price, its condition adjustment, and what it pays in cash and in trade. On each card you can:

- Tap **Cash** or **Trade** to choose how that card is paid. **All cash** and **All trade** set every card at once.
- Clear the checkbox to leave the card out (the seller keeps it).
- Type a **Price** when the catalog has no price for the card. The card is marked **Price entered**.

Cards worth less than the bulk threshold are marked **Bulk**. Depending on your settings they pay a flat rate, or start out unchecked.

### Market or low price

Use the **Price** switch at the top to build the offer on the **Market** price or the **Low** price (the lowest current listing). When a card has no low price, its market price is used and the card is marked **No low price — market used**.

> [!NOTE]
> Low prices are downloaded with each game's prices. If **Low** shows market prices for every card, refresh that game's prices in **Administration ▸ Catalog Data**.

## Show the seller

Tap **Customer view** for a full-screen offer you can turn toward the seller. It always shows the cash and store credit totals. An administrator chooses what else it shows: card pictures, card details, each card's price and offer, and the total market value.

![The customer view](intake-customer-view.png)

## Buy the cards

Tap **Buy** and choose who you're buying from:

- **Existing customer**: pick them from the list. Their current store credit is shown under their name.
- **New customer**: type their name; the customer is created when you buy.
- **No customer**: only offered when every card is paid in cash.

Add a **Note** if you like, then tap **Buy**. OmniCard:

- adds the cards to the intake location, each one costed at what you paid for it
- adds any trade value to the customer's store credit
- records the buy in the intake history

> [!WARNING]
> If prices changed while the quote was open (for example a price refresh ran), **Buy** stops and shows the new totals. Check them with the seller and tap **Buy** again.

You need the **Intake ▸ Buy cards** permission to buy. Staff with only **Scan and quote** can price cards but not buy them.

## Intake history

Click **History** on the Intake page to see every buy: when, who took it in, the customer, and the market value, cash and store credit paid. Click a row to see each card's condition, prices and what was paid for it. The card names link to the bought copies.

You need the **Intake ▸ View history** permission.

## Store credit

Every customer's store credit balance shows in the **Store credit** column on the **Customers** tab of [Sales](/sales). Click the amount to see every change, with links to the intakes that issued credit.

With the **Sales · Customers ▸ Adjust store credit** permission you can add credit, or take it off with a negative amount (for example when a customer spends it at the counter). Every adjustment needs a reason. A customer who still has store credit can't be deleted.

## Settings

Administrators set up intake in [Administration](/settings) on the **Intake** tab.

![The Intake settings](intake-settings.png)

- **Cash offer (%)** and **Trade offer (%)**: the share of the card's price paid in cash and in store credit. The defaults are 70% and 80%.
- **Condition adjustment**: the share of the price paid for each condition (NM, LP, MP, HP, DMG).
- **Bulk below**: cards worth less than this, after the condition adjustment, are bulk. Set it to 0 to turn bulk off.
- **Bulk cards**: **Pay a flat rate** (the **Flat rate per card**), or **Leave out of the offer**.
- **Round offers down to**: offers always round down, to whole cents or to 5¢, 10¢, 25¢, 50¢ or $1. **Round** chooses whether **Each card** or **Only the totals** are rounded.
- **Intake location**: where bought cards go. Until you choose one, staff can quote but not buy. Staff who buy need write access to the location's site.
- **Customer view**: what the seller sees on the customer view.

Click **Save** when you're done.

## Troubleshooting

- **Buy is greyed out**: a card still needs a price (type one or leave the card out), the quote is still updating, or no intake location is set.
- **"No intake location is set"**: an administrator needs to choose one on the **Intake** tab in Administration.
- **A card shows "No price"**: the catalog has no price for that printing. Type the price, or refresh the game's prices in **Administration ▸ Catalog Data**.
