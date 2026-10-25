# Customer kiosk

Put a tablet or computer in your store where customers browse the cards you choose, tap the ones they want, and send a pick list to the counter. Staff pull the cards and turn the list into an order.

## How the kiosk works

1. An administrator chooses which locations customers can browse, in **Administration ▸ Kiosk**.
2. An administrator sets up each kiosk device once by opening `/kiosk` on it.
3. A customer types their name on the kiosk and browses or searches the cards.
4. Each card the customer adds is **held** for them. No other kiosk, and no order, can take those copies while the hold lasts.
5. The customer taps **Send to counter**. The list appears on the **Kiosk** tab of [Sales](/sales).
6. Staff pull the cards and click **Create order**. The sale then continues like any other order. See [Sales](help:sales).

> [!NOTE]
> Names are unique while a list is open. If a customer picks a name someone else is using, the kiosk asks them to add an initial or a nickname.

## Choose what customers can browse

Open [Administration](/settings) and choose the **Kiosk** tab. Only administrators can see it.

![The Kiosk settings](kiosk-settings.png)

- **Kiosk open**: when off, every kiosk shows a "closed" screen.
- **Browsable locations**: click **Add location** to choose a location. Customers can browse and hold every card in these locations. Click a location's **x** to remove it.
- **Idle timeout (minutes)**: when a customer stops using the kiosk for this long, their unsent list is cleared and the cards are released. Lists already sent to the counter never expire.
- **Show prices to customers**: shows each card's market price, and the list total, on the kiosk.
- **Welcome message**: optional text shown on the kiosk's welcome screen.

Click **Save** when you're done.

The kiosk never offers these copies, even when they're in a browsable location:

- copies listed on TCGplayer or eBay
- copies already on an open order
- copies another customer is holding
- cards marked missing or traded away

## Set up a kiosk device

1. On the device, open your OmniCard address followed by `/kiosk`, for example `http://omnicard.local/kiosk`.
2. Enter a **Kiosk name**, such as "Front counter". Staff see this name next to each list.
3. Enter an administrator's username and password, then click **Make this a kiosk**.

![Setting up a kiosk device](kiosk-enroll.png)

The device now only runs the kiosk. It isn't signed in to OmniCard: opening the rest of the app on it shows the normal sign-in screen. The administrator's password isn't stored on the device.

> [!TIP]
> Most tablets have a kiosk or "guided access" mode that locks the screen to one page. Use it to keep customers on the kiosk.

### Take a device out of kiosk mode

- **On the device:** tap the **Card Kiosk** logo in the top-left corner five times quickly, then enter an administrator's username and password and click **Exit kiosk mode**.
- **From Administration:** on the **Kiosk** tab, under **Kiosk devices**, click **Revoke** next to the device. It stops working right away, and any unsent list on it is cleared. A revoked device can then be removed with **Delete**.

## What customers see

Customers type their name and tap **Start browsing**. They can:

- switch between games, if the browsable locations hold more than one
- search by card name, set code or set name
- sort by **Name** or, when prices are shown, by price
- tap a card to see a large image and its details
- tap **Add** to put a copy on their list

![Browsing cards on the kiosk](kiosk-browse.webp)

Each card shows how many copies are still available. Copies of the same printing in the same condition and language are shown together.

**My list** opens the pick list. Customers can change quantities, remove cards, and tap **Send to counter**. The kiosk then thanks them and returns to the welcome screen for the next customer.

![A customer's pick list](kiosk-pick-list.webp)

**Start over** clears the list and releases the cards. If nobody touches the kiosk for most of the idle timeout, it asks "Are you still there?" and clears the list a minute later.

## Fulfill a pick list

Open [Sales](/sales) and choose the **Kiosk** tab. The **Sales** menu item and the **Kiosk** tab show how many lists are waiting.

![Kiosk pick lists on the Sales page](kiosk-queue.png)

Lists sent to the counter come first, oldest first. Each card shows **Where** it is stored: the location, plus the page and slot for binders. Customers who are still browsing are shown below, greyed out.

1. Click **Start picking** so other staff know you have it. **Put back in queue** undoes this.
2. Pull the cards. If a card can't be found, or the customer changes their mind, click the bin icon to remove it, or the minus icon to remove one copy.
3. Click **Create order**. Keep **New customer** to create a customer with the name shown, or choose **Existing customer** and pick one.
4. The order is created with each card at the price the customer saw, and opens so you can finish the sale. See [Sales](help:sales).

To abandon a list, click **Cancel list**. Its cards become available again.

> [!WARNING]
> If a held card was sold or removed some other way, **Create order** stops and names the card. Remove it from the list, then try again.

## Permissions

Two permissions under **Sales · Kiosk** control the queue. See [Administration](help:administration).

- **View pick-list queue** shows the **Kiosk** tab.
- **Fulfill pick lists** allows claiming, editing and cancelling lists and creating orders from them.

Kiosk settings and devices are for administrators only.
