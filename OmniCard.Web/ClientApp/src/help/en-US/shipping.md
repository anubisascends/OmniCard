# Ship packages

Mark orders as shipped by scanning their shipping labels as the packages go out. Scan with your phone at the mailbox or post office, or with a webcam or handheld scanner at a shipping desk. You don't have to remember to update the board later.

## Before you start

The Ship page finds an order by its tracking number, so each order needs one before you scan:

1. Open the order on the [Sales](/sales) page.
2. Type the number in **Tracking**, or click **Scan label** (the barcode button in that box) and hold the label up to your camera.
3. Click **Save header**.

Scanning the label fills in the tracking number for you. If another order already has that tracking number, OmniCard warns you.

> [!NOTE]
> You need permission to ship orders. You have it if you can edit orders, or if an administrator gave you **Ship (scan labels)** under **Sales · Orders**. See [Administration](help:administration).

## Scan packages as they go out

1. Click **Ship** in the menu.
2. Allow camera access when your browser asks.
3. Point the camera at the barcode on the shipping label. Fit the barcode inside the white box.

You hear a beep, your phone vibrates, and the result shows under the camera:

| Result | Meaning |
|---|---|
| **Shipped** | The order is now in your shipped lane. |
| **Ready to ship** | One order matches. Click **Mark shipped** to ship it. Shown when **Auto-ship** is off. |
| **Already shipped** | That order was shipped earlier. Nothing changes. |
| **… open orders use this tracking number** | More than one order has this tracking number. Click **Mark shipped** on the one you're sending. |
| **No open order has tracking number …** | No order in **Created** or **Packed** has this number. Add it to the order and scan again. |

Shipping an order from this page does the same as dragging it into the shipped lane on the board: OmniCard records the sale and removes the sold cards from your collection. If you've added your own lanes, the order goes into the first lane that ships orders.

The **This session** list shows everything you scanned since you opened the page, with a count of shipped orders. Click **Open order** to see an order's details.

> [!TIP]
> Barcodes from USPS, UPS and FedEx all work. USPS and FedEx labels hold extra routing digits around the tracking number, and OmniCard ignores them.

## Auto-ship

Turn on **Auto-ship** to ship each order as soon as you scan its label, without clicking **Mark shipped**. This suits a shipping desk with a webcam, where you hold up one package after another.

OmniCard still asks you to choose when more than one order has the same tracking number.

> [!WARNING]
> Shipping can't be undone from the Ship page. Moving an order back on the board doesn't return its cards to your collection. Leave **Auto-ship** off if you'd like to check each package.

Your **Auto-ship** and **Camera** choices are remembered on each device.

## Use a webcam or handheld scanner

- **More than one camera:** choose the camera you want under the video. OmniCard remembers it on this computer.
- **Handheld barcode scanner:** turn **Camera** off, click in **Tracking number**, and scan. Most handheld scanners press Enter for you. You can also type a number and press Enter.
- **Dark spot:** on phones that have one, tap the flashlight button on the video.

## Troubleshooting

- **The camera doesn't start:** check that your browser is allowed to use the camera. The camera only works when you open OmniCard over a secure address (starting with `https://`). If it can't start, type the tracking number or use a handheld scanner instead.
- **A label won't scan:** move closer so the barcode fills most of the white box, and avoid glare. Or type the number.
- **No open order found:** check the order's **Tracking** box against the label. If the order was moved back to **Created** or **Packed** after shipping, scanning ships it again.
