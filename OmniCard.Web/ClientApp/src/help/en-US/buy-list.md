# Buy List

The Buy List holds the cards the collection's owner wants to buy. Each game has its own list. When a wanted card turns up in a scan or an intake, OmniCard flags it so nobody passes it up, and customers can see what you're looking for on the kiosk.

## Who can do what

- **Everyone** can open the [Buy List](/buy-list) and search the collection for wanted cards.
- **Administrators** add, edit and remove cards, and choose where wanted copies go.

The list follows the game in the top bar. **All Games** shows every game's list together.

![The Buy List page with a Grail card, its set-aside location and the Wanted and Fulfilled tabs](buy-list-page.png)

## Add a card

1. Open [Buy List](/buy-list) and select **Add card**.
2. Search for the card and pick it.
3. Choose how the card should match:
   - **Copies wanted**: how many copies you want.
   - **Printing**: **Any printing**, or only the printing you picked.
   - **Finish**: **Any finish**, **Foil only** or **Non-foil only**.
   - **Priority**: **Normal**, **High** or **Grail**.
   - **Note for staff**: shown in scans and intakes. Customers never see it.
4. Select **Add to Buy List**. The dialog stays open for the next card.

Adding a card that's already on the list raises its **Copies wanted** instead of adding a second row.

To change a card later, select the pencil. To take it off the list, select the trash can.

> [!NOTE]
> **Any printing** matches by card name, so printing details in the name, such as "(Parallel)" or "- 199/165", don't get in the way.

## How a card is flagged

On the [Scan](/scan) and [Intake](/intake) pages, a wanted card gets:

- a gold frame on its row (heavier for **High** and **Grail**)
- a chip such as **Buy List Grail: want 2**
- a banner at the top of the list counting the wanted cards, with **Show only these** to filter the list down to them
- a panel on the card with its priority, the staff note and where it will go

![A scan list with a Buy List card framed in gold](buy-list-scan.png)

If you correct a card by hand, OmniCard checks the Buy List again for the corrected card. Location audits don't show the flag.

> [!TIP]
> The flag shows on every copy of a wanted card, even if you've scanned more copies than you want. OmniCard works out which copies count when you add or buy them.

## When copies come in

Adding cards from the Scan page, a scan batch or an intake counts them off the list. **Copies wanted** goes down, and when it reaches 0 the card moves to **Fulfilled**. It stops being flagged and leaves the kiosk.

Only the copies you still want count. If you want 1 and bring in 3, the other 2 are ordinary copies.

Adding a card any other way, such as **Add card** on a location or an import, doesn't touch the Buy List.

To want a fulfilled card again, open **Fulfilled**, select the pencil and raise **Copies wanted**.

## Set wanted copies aside

An administrator can give each game a **Set-aside location**. Choose a game in the top bar, then pick the location at the top of the [Buy List](/buy-list).

Wanted copies then go to that location when they're added or bought, instead of the location they'd normally go to. If only some copies of a stack are wanted, the stack is split.

If you can't add cards to the set-aside location's site, the copies stay with the rest of the cards and OmniCard tells you so. They still count off the list.

## Pay more for wanted cards

An administrator can set a separate **Buy List rate** in [Settings ▸ Intake](/settings). See [Intake settings](help:intake#settings).

## Find wanted cards in the collection

Select **Find in collection** to see every copy you already own that's on the Buy List. You can also type `is:buylist` in any collection or location search, and combine it with other terms such as `is:buylist loc:binder`. See [Search syntax](help:search-syntax#is-flags).

The search and the **in collection** count only include sites you can see.

## On the kiosk

Customers can open **Cards we're looking for** from the kiosk's welcome screen or while browsing. They see each wanted card's art and name, plus any wanted printing or finish. **High** cards are marked **Really want** and **Grail** cards **Most wanted**. They never see quantities, prices or notes.

An administrator can turn this off with **Show the Buy List to customers** in [Settings ▸ Kiosk](/settings). See [Customer kiosk](help:kiosk).

> [!WARNING]
> Customers who've seen this list know which cards you want. Keep that in mind if you pay a higher Buy List rate.
