# Labels

Print labels to stick on your card holders, such as toploaders, semi-rigids or one-touch cases. You design the labels yourself: choose the label sheet, set its margins, how many labels it holds and the space between them, then lay out a label with card details, QR codes and barcodes. Print labels for the cards you select, or for every card in a location.

## Before you start

- **Templates** are shared. Everyone who can print labels can use every template.
- To create or change templates you need the **Manage label templates** permission. To print you need **Print labels**. Administrators have both. See [Administration](help:administration#the-permissions).
- Labels are made as a PDF in your browser. It opens in a new tab, where you print it. If your browser blocks pop-ups, the PDF downloads instead.

## Create a label template

1. Go to **Administration ▸ Labels**.
2. Click **New template**. The label designer opens.
3. Enter a **Template name**, for example *Toploaders – 30 per sheet*.
4. Set up the sheet (see below).
5. Lay out the label (see [Design the label](#design-the-label)).
6. Click **Test print** to check it, then click **Save**.

![The Labels tab listing two label templates](labels-templates.png)

On the **Labels** tab, click the pencil to change a template, the copy button to start a new template from an existing one, or the bin to delete one.

## Set up the sheet

The left side of the designer describes the paper the labels are printed on. Every label on a sheet is the same size, and OmniCard works out that size for you.

| Setting | What it means |
|---|---|
| **Start from** | Fills in a common layout to start from: 30 or 80 labels on US Letter, 65 labels on A4, or one label per page for a label printer. You can change every value afterwards. |
| **Page size** | US Letter, US Legal, A4 or A5. Or type your own **Page width** and **Page height**. |
| **Margins** | The space from each edge of the page (**Top**, **Bottom**, **Left**, **Right**) to the first label. |
| **Columns** and **Rows** | How many labels fit across and down the sheet. |
| **Space between columns** | The gap between two labels side by side. |
| **Space between rows** | The gap between two rows of labels. |

Use the **IN** and **MM** buttons to type sizes in inches or millimetres. Below the settings, **Each label** shows the label size that results and how many labels fit on a sheet. If the margins and spacing leave no room, a warning appears and you can't save.

> [!TIP]
> Copy the measurements from your label sheet's packaging or the maker's template. For a label printer that prints one label at a time, set the page to the size of one label, with no margins, 1 column and 1 row.

## Design the label

The middle of the designer shows one label. Drag elements to move them, and drag their corners to resize them. Click an element to change its settings, such as text size, alignment or colour, in the panel on the right. The **Field List** on the right lists every element on the label.

![The label designer: sheet settings on the left, the label in the middle and the Field List on the right](labels-designer.png)

A new template starts with a QR code that opens the card's page, the card name, its set code and collector number, and its market price.

### Fill in card details

An element is filled in from the card when its **Name** is one of the field names below. Under **Add a field**, click a field to add it to the label already named. You can also add an element from the toolbar beside the label and type a field name into its **Name**. To print the same field twice, add a number to the end of the name, for example *cardName2*.

| Field | Name | Prints |
|---|---|---|
| Card name | cardName | The card's name |
| Game system | game | For example *Magic: The Gathering* |
| Set code | setCode | For example *M11* |
| Set name | setName | For example *Magic 2011* |
| Collector number | collectorNumber | The number printed on the card |
| Rarity | rarity | The card's rarity |
| Condition | condition | NM, LP, MP, HP or DMG |
| Language | language | For example *EN* or *JA* |
| Finish | finish | *Foil*, *Etched* and so on. Blank for non-foil cards. |
| Market price | marketPrice | The current market price. Blank when there's no price. |
| Location | location | The location the card is in |
| Card page link | cardUrl | A link to the card's page in OmniCard (see [Card pages](#card-pages)) |
| TCGplayer link | tcgplayerUrl | The card's TCGplayer page |
| Card ID | cardId | The card's ID in OmniCard |
| TCGplayer product ID | tcgplayerId | The card's TCGplayer product number, when it's known |

The links are added as QR codes and the IDs as Code 128 barcodes. Any element can show any field. For example, a text element named *tcgplayerUrl* prints the link as text.

To put several fields on one line, add a **Text with fields** element and type the fields in braces, for example *{setCode} · #{collectorNumber}*. The designer shows it with sample values.

An element whose name isn't a field, such as a heading you type yourself, prints exactly what the designer shows.

### QR codes and barcodes

The toolbar beside the label adds QR codes and barcodes: Code 128, Code 39, UPC-A, UPC-E, EAN-13, EAN-8, ITF-14, Data Matrix (GS1) and PDF417. It also adds text, images, SVG graphics, lines, rectangles and ellipses. Hover over a button to see what it adds.

Name a barcode after a field to fill it in from the card, or type its value in the designer. UPC and EAN codes only accept the right number of digits. A barcode whose value doesn't fit its type is left off the label.

### Test print

Click **Test print** to make a sheet filled with a sample card, with every label outlined. Print it on plain paper and hold it up to the light against a label sheet. If the labels don't line up, adjust the margins and spacing and try again.

## Print labels

You can print labels from the [Collection](help:collection) page or from a location's page.

- **For some cards:** on the Collection or a location page, click **Select**, tick the cards, then click **Print labels**.
- **For a whole location:** on the location's page, click **Print labels** at the top. Every card the page shows is printed, in binder order (page and pocket, then name). If you've typed a search or picked a game, only those cards are printed.

![The Print labels dialog with a template chosen and four labels already used on the first sheet](labels-print-dialog.png)

In the **Print labels** dialog:

1. Choose the **Label template**. OmniCard remembers the one you used last.
2. Choose **One label per copy** to print a label for every copy (a stack of 3 gets 3 labels), or **One label per card** for one label per entry.
3. If some labels on your first sheet are already used, enter how many in **Labels already used on the first sheet**. Printing starts at the next label, counting left to right, then top to bottom.
4. Turn on **Draw label outlines** to print a thin border around each label, for example to cut labels from plain paper.
5. Check **Card links open** (see [Card pages](#card-pages)).
6. Click **Create PDF**, then print the PDF from the new tab. Print at actual size (100%), not "fit to page".

![A printed sheet: the first four labels are left empty, then one label per card with a QR code, name, set and number, and price](labels-printed-sheet.png)

## Card pages

Every card in your collection has its own page in OmniCard showing its game, set, number, condition, language, finish, quantity, location and market price, with a **TCGplayer** button. If you're allowed to edit cards, **Edit card** opens the card's details. Scanning the card page QR code on a label with a phone opens this page. You sign in first if you need to.

![A card's page on a phone, opened from a label's QR code](labels-card-page.png)

**Card links open** in the **Print labels** dialog is the address the QR codes point to. It starts as the address you're using OmniCard at. If that address only works on this computer (such as *localhost*), a warning appears. Type the address a phone on your network uses to reach OmniCard instead, for example *http://omnicard.local/app*. OmniCard remembers it.

> [!NOTE]
> A card's page only works while the card is in your collection. If the card is sold, traded or deleted, its QR code opens a message saying the card isn't in the collection.

## Related topics

- [Collection](help:collection): selecting cards
- [Locations](help:locations): a location's page
- [Administration](help:administration): the Labels tab and permissions
