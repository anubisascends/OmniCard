import{g as J,a as K,r as f,u as X,b as Y,c as N,j as n,d as Z,s as W,B as ce,m as ee,e as P,F as de,f as p,D as le,P as j,T as he,h as pe,i as H,k as z,l as ue,n as y,o as x,p as me,I as ge,C as ye,q as we,A as B,t as fe,L as T,v as b,w as be,x as _,S as I,y as ke,z as ve,E as Ce,G as V,H as q,J as xe,K as Te,M as Se,N as Ae,O as Ie,Q as _e}from"./index-C7aA-0j5.js";function Oe(e){return J("MuiCardActionArea",e)}const M=K("MuiCardActionArea",["root","focusVisible","focusHighlight"]),Be=e=>{const{classes:t}=e;return Z({root:["root"],focusHighlight:["focusHighlight"]},Oe,t)},Le=W(ce,{name:"MuiCardActionArea",slot:"Root",overridesResolver:(e,t)=>t.root})(ee(({theme:e})=>({display:"block",textAlign:"inherit",borderRadius:"inherit",width:"100%",[`&:hover .${M.focusHighlight}`]:{opacity:(e.vars||e).palette.action.hoverOpacity,"@media (hover: none)":{opacity:0}},[`&.${M.focusVisible} .${M.focusHighlight}`]:{opacity:(e.vars||e).palette.action.focusOpacity}}))),Pe=W("span",{name:"MuiCardActionArea",slot:"FocusHighlight",overridesResolver:(e,t)=>t.focusHighlight})(ee(({theme:e})=>({overflow:"hidden",pointerEvents:"none",position:"absolute",top:0,right:0,bottom:0,left:0,borderRadius:"inherit",opacity:0,backgroundColor:"currentcolor",transition:e.transitions.create("opacity",{duration:e.transitions.duration.short})}))),Me=f.forwardRef(function(t,o){const a=X({props:t,name:"MuiCardActionArea"}),{children:s,className:i,focusVisibleClassName:h,slots:r={},slotProps:l={},...d}=a,c=a,g=Be(c),u={slots:r,slotProps:l},[w,m]=Y("root",{elementType:Le,externalForwardedProps:{...u,...d},shouldForwardComponentProp:!0,ownerState:c,ref:o,className:N(g.root,i),additionalProps:{focusVisibleClassName:N(h,g.focusVisible)}}),[v,k]=Y("focusHighlight",{elementType:Pe,externalForwardedProps:u,ownerState:c,ref:o,className:g.focusHighlight});return n.jsxs(w,{...m,children:[s,n.jsx(v,{...k})]})});function Ee(e){return J("MuiTableContainer",e)}K("MuiTableContainer",["root"]);const De=e=>{const{classes:t}=e;return Z({root:["root"]},Ee,t)},Ne=W("div",{name:"MuiTableContainer",slot:"Root",overridesResolver:(e,t)=>t.root})({width:"100%",overflowX:"auto"}),Fe=f.forwardRef(function(t,o){const a=X({props:t,name:"MuiTableContainer"}),{className:s,component:i="div",...h}=a,r={...a,component:i},l=De(r);return n.jsx(Ne,{ref:o,as:i,className:N(l.root,s),ownerState:r,...h})}),ne=P(n.jsx("path",{d:"M20 11H7.83l5.59-5.59L12 4l-8 8 8 8 1.41-1.41L7.83 13H20z"}),"ArrowBack"),te=P(n.jsx("path",{d:"m12 4-1.41 1.41L16.17 11H4v2h12.17l-5.58 5.59L12 20l8-8z"}),"ArrowForward"),Ue=`# Administration

The Administration page is where you change your own password and, depending on your access, manage users, roles, sites, catalog data and OmniCard's settings. This topic explains every tab and who can see it.

## Open Administration

Click **Administration** in the sidebar. Everyone can open this page, but you only see the tabs your account allows. Administrators can also open **Account ▸ Account & users** from the top bar to jump straight to the **Users** tab.

![The Administration page with its row of tabs](administration-tabs.png)

| Tab | What it's for | Who sees it |
|---|---|---|
| **Sales** | Where picked sale cards are moved | People with the Settings view permission |
| **Receipts** | Store details and layout for printed receipts | People with the Settings view permission (only administrators can change it) |
| **Scan Badges** | Value badges on the Scan page, plus watched scan folders | People with the Settings view permission (badges and folders can only be changed by administrators) |
| **Deck Types** | Deck formats and their rules | People with the Deck Types view permission |
| **Appearance** | Card preview size | Everyone |
| **Catalog Data** | Download card catalogs, prices and artwork | People with the Catalog Data view permission |
| **eBay** | Connect eBay and set seller details | People with the eBay view permission |
| **Roles** | Permission bundles | Administrators only |
| **Sites** | Major physical places and who can see them | Administrators only |
| **Users** | Your password, and (for administrators) all accounts | Everyone |
| **Components** | Software versions and licenses | Everyone |

If the page scrolls sideways on a small screen, use the arrows at either end of the tab row to see more tabs.

## Change your password

Anyone can change their own password.

1. Go to **Administration ▸ Users**.
2. Under **Your password**, enter your **Current password**.
3. Enter a **New password**, then type it again in **Confirm new password**.
4. Click **Change password**. You'll see *Password changed.*

If the two new passwords don't match, the form says *Passwords don't match.* If your current password is wrong, you'll see *Current password is incorrect.* If you've forgotten your password, ask an administrator to reset it.

## Users

Administrators see a list of every account below **Your password**, with each person's **Username** and **Role**.

- **system** marks the built-in Admin account. It can't be deleted and always has full access, but you can reset its password.
- **custom** means the person has extra permissions given or taken away on top of their role.

![The Users tab with the list of accounts](administration-users.png)

### Add a user

1. Click **Add user**.
2. Enter a **Username**, a **Password** and **Confirm password**.
3. Choose a **Role**. If you leave it as **No role**, the new user gets the *Viewer* role, which can look at everything but change nothing.
4. Tick **Administrator (full access)** instead if this person should be able to do everything, including managing users.
5. Click **Create**.

### Change what a user can do

1. Click the **Edit access** button (pencil) on the user's row.
2. Tick or untick **Administrator (full access)**. Administrators skip roles and permissions entirely.
3. For everyone else, choose a **Role**.
4. Under **Also allow (grant)**, tick extra permissions this person should have beyond their role.
5. Under **Never allow (deny)**, tick permissions to take away from this person even if their role includes them.
6. Click **Save**.

Changes take effect straight away. The person doesn't need to sign out and back in. Their sidebar updates the next time they move to another page.

### Reset a password

1. Click the **Reset password** button (key) on the user's row.
2. Enter the new **Password** and **Confirm password**.
3. Click **Reset**, then tell the person their new password. They can change it themselves afterwards.

### Delete a user

Click the **Delete user** button (bin) on the row and confirm. The built-in Admin account can't be deleted.

## Roles

A role is a reusable bundle of permissions you assign to users. Only administrators see this tab.

OmniCard comes with three built-in roles. You can change their permissions, but you can't rename or delete them.

| Role | What it allows |
|---|---|
| **Administrator** | Every permission |
| **Viewer** | Looking at every section, without changing anything. New users get this role by default |
| **Staff** | Everyday work: viewing everything, scanning, editing cards, creating locations, lists, trades, imports, orders and listings. No deleting and no settings |

### Add or edit a role

1. Click **Add role**, or the **Edit** button (pencil) on a role's row.
2. Enter a **Name**. Built-in roles keep their name.
3. Tick the permissions the role should have. Ticking a section's heading ticks every permission in that section.
4. Click **Save**.

To delete a role you created, click its **Delete** button (bin). People who had that role keep any permissions given to them personally, but lose the role's permissions.

### The permissions

Permissions are grouped by section. Most sections have **View**, plus actions such as **Create**, **Edit** and **Delete**.

| Section | Permissions |
|---|---|
| Dashboard | View |
| Scan | View, Commit scans |
| Collection | View, Edit, Delete, Export |
| Locations | View, Create, Edit, Delete |
| Binder | View, Edit |
| Sets | View, Export want list |
| Inventory | View, Create, Edit, Delete |
| Lists | View, Create, Edit, Delete, Commit to collection |
| Trades | View, Create, Finalize, Cancel |
| Import | Run import |
| Export | Run export |
| Sales · Orders | View, Create, Edit, Delete, Import orders |
| Sales · Customers | View, Create, Edit, Delete |
| Sales · Listings | View, Create, Edit, Delete, Pick / mark picked |
| Settings | View, Edit |
| Deck Types | View, Edit |
| Catalog Data | View, Refresh |
| eBay | View, Manage / connect |

Managing users, roles and sites isn't a permission. Only administrators can do it.

## Sites

A site is a major physical place, such as a home, a shop or a storage unit, that holds many locations. Use sites when several people share one OmniCard and shouldn't all see everything. Only administrators see this tab.

The **Default** site is always visible to every user and holds every location that isn't assigned elsewhere, including Bulk. It can be renamed but not deleted, and it has no access list.

### Add or edit a site

1. Click **Add site**, or the **Edit** button (pencil) on a site's row.
2. Enter a **Name**, for example "The Shop", and an optional **Description**.
3. Click **Save**.

New locations are created in a site from the [Locations](help:locations) page. You can also move an existing location to another site there.

### Choose who can see a site

1. Click the **Who can see this site** button (people icon) on the site's row.
2. For each role and each user, choose **None**, **Read** or **Write**:
   - **Read** lets them browse and search the site's locations and cards, and add those cards to lists.
   - **Write** also lets them change those locations and cards, as far as their normal permissions allow.
3. Click **Save**.

A person gets the higher of their own level and their role's level. Administrators always see every site.

![Choosing who can see a site, with None, Read and Write for each role and user](administration-site-access.png)

### Delete a site

1. Click the **Delete** button (bin) on the site's row.
2. If the site has locations, choose a site under **Move its locations to**. Its locations and all their cards move there.
3. Click **Delete**.

## Catalog Data

OmniCard recognizes and prices cards using a catalog for each game, stored on the server. This tab refreshes those catalogs. You need the Catalog Data view permission to see it, and the Refresh permission to run anything.

![The Catalog Data tab with a download in progress](administration-catalog-data.png)

### Refresh a catalog

1. Choose a **Game**.
2. Click one of the jobs:
   - **Download catalog** gets the latest card list, including new sets. Run this first on a new server, and again when a new set comes out.
   - **Update prices** refreshes market prices.
   - **Recompute hashes** rebuilds the image fingerprints OmniCard uses to recognize scanned cards. It only processes cards that don't have one yet. **Download catalog** already does this for new cards, so you'll rarely need it on its own.
   - **Download artwork** saves a copy of every card image on the server, so pictures load quickly and don't depend on outside websites.
3. Watch the progress message. When it finishes, the job appears under **Recent** with ✓ if it worked or ✗ and an error message if it didn't.

Only one job runs at a time, for all games. While a job is running, the buttons are greyed out. Large catalogs, especially Magic: The Gathering, can take a long time.

> [!NOTE]
> A job that finishes very quickly hasn't necessarily failed. If nothing has changed since the last run, there's little to do. Check the ✓ under **Recent**.

### Languages to download

Under the buttons, **Languages to download** controls which non-English printings the chosen game's catalog includes. Downloading them lets scans of foreign cards match their own artwork.

- **English** is always included.
- Tick or untick languages. Your choice saves straight away, but only takes effect the next time you click **Download catalog**. Unticked languages are removed from the catalog then.
- For Magic: The Gathering, adding any non-English language switches to a much larger download (about 400 MB).
- Non-English cards rarely have their own prices, so they're valued at the English printing's price. Japanese Pokémon cards are the exception: they have real prices.
- Some games' sources only provide English cards. For those games you'll see a note instead of checkboxes. You can still record a foreign copy's language when you scan or edit it.

## Scan Badges

This tab has two parts: value badges on the Scan page, and watched scan folders.

### Scan badges

On the Scan page, matched cards show a gold star when the card isn't in your collection yet, and one to five currency signs showing how valuable it is. Here you set how those signs are worked out. Only administrators can change these settings.

1. Enter the **Currency code (ISO 4217)**, for example \`USD\`, \`EUR\` or \`GBP\`. This picks the currency sign.
2. Fill in **Tier 1 max** to **Tier 4 max**. A card worth up to the tier 1 amount shows one sign, up to the tier 2 amount shows two, and so on. Anything above tier 4 shows five.
3. Check the **Preview**, which lists the price range for each number of signs.
4. Click **Save**.

The amounts should go up from tier 1 to tier 4. If they don't, OmniCard warns you and sorts them when you save. See [Scanning cards](help:scanning).

### Watched scan folders

Only administrators see this part. It points OmniCard at folders on the server where a document scanner saves its images. Each subfolder becomes a scan batch that's matched in the background and waits for review on the Scan page. See [Scan batches](help:scan-batches).

![The Watched scan folders settings for each game](administration-scan-folders.png)

1. Turn on **Watch folders**.
2. Set the **Quiet period (seconds)**. This is how long OmniCard waits after the last new file arrives before it starts matching a batch.
3. Set **Keep images (days)**. Stored scans of batches that were added or discarded are deleted after this many days.
4. For each game you scan, enter the **Folder on the server**, for example \`D:\\Scans\\Mtg\`, and make sure **Active** is on.
5. Once a folder is entered, choose the defaults for that game's batches: **Sets (art fallback)**, **Condition**, **Card language** and **Foil**.
6. Optionally, click **Choose…** next to **Default location** to pre-select where that game's batches are added. **Clear** removes it.
7. Click **Save**.

A chip next to each folder shows its status:

| Status | Meaning |
|---|---|
| **Found** | The folder exists and OmniCard can use it |
| **Folder not found** | The path is wrong, or the server can't reach it |
| **Can't move files** | OmniCard can read the folder but can't move processed files into its \`_processed\` subfolder. Check the folder's permissions |

Save each batch into its own subfolder, named after the batch. When OmniCard picks up a file, it moves the original into a \`_processed\` subfolder.

> [!NOTE]
> Folders are only watched while the OmniCard server is running. If batches only appear after someone opens OmniCard in a browser, ask whoever runs the server to keep it always running.

## Receipts

Set up the receipts you print for sales orders. They're sized for thermal or roll printers. You print a receipt from an order's detail panel on the [Sales](help:sales) page. Only administrators can change these settings.

1. Under **Company / store details**, fill in your **Store name**, **Email**, **Phone** and address.
2. Under **Logo**, click **Upload logo…** to add your logo, or **Remove logo** to take it off. PNG, JPG, GIF, WEBP and BMP images up to 5 MB are accepted.
3. Under **Printer layout**, set the **Paper width (mm)**, or click the **58 mm** or **80 mm** shortcut. Then set the **Margin (mm)** and **Font size (pt)**.
4. Turn **Show prices** off if you don't want prices on the receipt, for example for gift receipts.
5. Add any **Footer text**, for example "Thanks for your business! All sales final."
6. Check the preview, which is drawn at roughly the paper width you chose.
7. Click **Save**.

## Sales

This tab controls what happens when you mark a listing as picked on the Sales page. See [Sales](help:sales).

- **For-sale location** is where picked cards are moved, for example a "For sale" box. Click **Change** to pick a location, or **Clear** to remove it.
- **Move picked cards to the for-sale location** turns the automatic move on or off. When it's off, marking a listing as picked only changes its status and the card stays where it is.

Changing these settings needs the Settings edit permission.

## Deck Types

Deck types are the formats a deck box can be assigned, such as Commander or Standard. They're set up per game. Their rules only produce warnings in a deck box. They never stop you adding cards. See [Deck boxes](help:deck-boxes).

1. Choose a **Game**.
2. The table lists that game's deck types with their **Size**, **Copies** and **Commander** slots. Built-in types are marked **built-in**.
3. Click **Add deck type** to create your own, or the pencil on a row to edit one. The bin deletes it. Deck boxes that used a deleted type are left without a type.

When adding or editing a deck type, you can set:

| Field | What it means |
|---|---|
| **Name** | The format's name |
| **Deck size min** and **Deck size max** | How many cards the deck should have |
| **Max copies / card** | How many copies of one card are allowed |
| **Commander/leader slots** | How many commander or leader cards the format uses |
| **Singleton (max 1 of each card)** | Only one copy of each card is allowed |
| **Basic lands exempt from copy limit** | Basic lands don't count toward the copy limit |
| **Count copies by card number** | All the different artworks of the same card number count as one card |

Changing deck types needs the Deck Types edit permission.

## Appearance

Choose how large the card picture grows when you hover over a card in a list. Drag the slider from 100% (the default) up to 300%. The text under the slider shows the popup's size in pixels.

This setting is saved in your current browser only. It doesn't affect other people or your other devices.

## eBay

Connect OmniCard to your eBay seller account so you can list cards on eBay. See [eBay](help:ebay) for the full guide. You need the eBay view permission to see this tab, and the Manage / connect permission to change anything.

### Connect your account

The **Status** shows one of:

- **Connected**: OmniCard is linked to your eBay account.
- **Not connected**: you can click **Connect to eBay**. You're sent to eBay to sign in and approve access, then brought back here.
- **Not configured**: the server is missing eBay app credentials. Whoever runs the server needs to add them first.

When you're connected, **Run seller setup** creates your eBay inventory location and business policies from the seller settings below. **Disconnect** unlinks your account.

### Seller settings

Fill these in before you run seller setup.

1. Under **Inventory location address**, enter at least **Address line 1**, **City**, **Postal code** and **Country (ISO-2, e.g. US)**. eBay needs a complete address.
2. Under **Shipping policy**, turn on **Free shipping** or enter a **Flat shipping cost**. Then set the **Handling time (days)** and choose a **Shipping service**.
3. Under **Return policy**, choose whether to **Accept returns**, the **Return window (days)**, and who pays return shipping (**Buyer** or **Seller**).
4. Click **Save seller settings**, then click **Run seller setup** above.

**Setup status** shows whether the inventory location and the three business policies have been created, and when setup last completed.

## Components

This tab lists the software and third-party components that OmniCard ships with or runs on, grouped by category. Each row shows the **Version** and **License**, with links to the project's **Website** and **License**. Everyone can see it.

## Related topics

- [Getting started](help:getting-started): signing in and first-time setup
- [Troubleshooting](help:troubleshooting): missing sections, prices and images
- [Scan batches](help:scan-batches): reviewing scans from watched folders
- [Locations](help:locations): creating locations inside sites
- [eBay](help:ebay) and [Sales](help:sales): selling cards
`,Re=`# Binders

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
- Use the search box to filter the list. It accepts the full search syntax, such as \`set:mh3\`, \`t:dragon\` or \`tag:trade\`. See [Search syntax](help:search-syntax).
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
`,We=`# Collection

The Collection page lists every single card you own across all your storage locations. Use it to search, sort, edit, move, list for sale, export, and check a decklist against what you have.

## What the Collection page shows

Open [Collection](/collection) from the navigation menu. The page has three parts:

- A **search box** at the top. Type a search and press **Enter** to filter the list.
- A toolbar with the **Stack duplicates** switch and the **Select** button.
- The card list itself, one card (or stack of identical cards) per row.

The game selector in the top bar decides which game's cards you see. Choose a single game, or **All Games** to see everything at once.

![The Collection page with the search box, toolbar and card list](collection-overview.png)

> [!NOTE]
> You only see cards in sites you have access to. If cards you expect are missing, ask an administrator whether you can view that site. See [Locations](help:locations).

### Columns

| Column | What it shows |
|---|---|
| Name | Card name. Hover over it to see the card art. |
| Set | Set code. |
| No. | Collector number. |
| Rarity | Printed rarity. |
| Cond | Condition: NM, LP, MP, HP or DMG. |
| Lang | A language badge for non-English copies. Blank means English. |
| Foil | A check mark for foil copies. |
| Qty | How many copies the row holds. |
| Market | Current market price per copy, in US dollars. |
| Status | **Listed** or **Picked** if the card is listed for sale. |
| Location | The storage location the card is in. |

Hover over a **Listed** or **Picked** badge to see a reminder that you change listings from **Sales ▸ Listings**.

## Search your collection

1. Click in the search box.
2. Type a card name, or a search using fields such as \`t:creature\`, \`set:dom\` or \`tag:trade\`.
3. Press **Enter**.

Plain words match the card name, so \`bolt\` finds Lightning Bolt. Combine terms with spaces, use \`or\` for alternatives, and put \`-\` in front of a term to exclude it. For example, \`t:dragon -is:foil\` finds non-foil dragons.

Click the **?** icon at the right of the search box to see the fields available for the selected game, with an example for each.

For the complete list of fields and operators for every game, see [Search syntax](help:search-syntax).

To clear a search, delete the text and press **Enter** again.

## Sort the list

Click a column header to sort by that column. Click it again to reverse the order. Sorting covers your whole collection, not just the page you're looking at, so sorting by **Market** from highest to lowest shows your most valuable cards first.

The list is sorted by **Name** until you choose another column. The **Status** column can't be sorted.

Use the controls at the bottom of the list to move between pages and to show 25, 50 or 100 rows per page.

## Stack duplicates

The **Stack duplicates** switch is on by default, and OmniCard remembers your choice.

- **On:** identical cards (same name, set, collector number and foil) appear as one row. **Qty** shows the total, and the name shows how many separate entries were combined, for example *· 3 printings*.
- **Off:** every entry appears on its own row, even if it's the same printing stored in two places.

> [!TIP]
> Turn **Stack duplicates** off when you want to edit or move one particular copy. Clicking a stacked row opens just one of the combined entries.

## Preview card art

Hover over a card's name to see a larger image of the card. Foil cards show an animated rainbow sheen so you can tell them apart at a glance.

To make the preview bigger or smaller, go to **Administration ▸ Appearance** and adjust the card preview size.

## View and edit a card

Click any row (when you're not in Select mode) to open the card details panel on the right.

![The card details panel with condition, language, foil, quantity and location](collection-card-details.png)

The top of the panel shows the card name, set, collector number, rarity and art. A chip shows the current **Market** price, and another chip appears if the card is **Listed for sale** or **Picked for sale**.

### Change a card's details

1. Click the card to open the details panel.
2. Change any of the fields below.
3. Click **Save**. Click **Cancel** to close without saving.

| Field | What it does |
|---|---|
| Condition | NM, LP, MP, HP or DMG. |
| Language | The language of this copy. The list shows the languages available for the card's game. |
| Foil | Turn on for a foil copy. |
| Quantity | How many copies this entry holds (at least 1). |
| Purchase price | What you paid per copy. Optional. |
| Note | Free text, for example *signed, played, misprint*. |
| Location | Where the card is stored. Click **Change** to pick another location. |
| Tags | Your own labels. Pick an existing tag or type a new one and press Enter. |

Moving a card with **Change** takes effect when you click **Save**. Tags you add here can be searched with \`tag:\`. See [Search syntax](help:search-syntax).

### Pick a location

When you click **Change** (or **Move to…** for several cards), the location picker opens.

- Type in **Search locations…** to filter the list. Locations are grouped by type.
- Click a location to choose it.
- Click **New location** to create one without leaving the picker. Enter a **New location name**, choose the type (and the site, if you can add to more than one), then click **Create & select**. A new deck box also needs its game.

A deck box that holds a different game is shown but can't be chosen. For more about locations and sites, see [Locations](help:locations) and [Deck boxes](help:deck-boxes).

### Read-only cards

If a card is in a site you can only view, the panel shows a message and every change is disabled. Ask an administrator for write access to that site if you need to edit it.

### Split a stack

When an entry holds more than one copy, the panel shows **Split stack**. Splitting is useful when each copy needs its own binder pocket or its own sale listing.

1. Click **Split stack**.
2. Choose one of these:
   - Enter the number of **Copies to move to a new stack**, then click **Split stack**.
   - Click **Split into N singles** to turn every copy into its own entry.
3. The new entries stay in the same location. In a binder, they go to the binder's Unplaced pool. See [Binders](help:binders).

You can't split a card that's listed for sale. Unlist it from **Sales ▸ Listings** first.

### Add a card to a trade

Click **Add to trade** to put the card into your current trade. A message confirms it was added. Finish the trade on the [Trades](/trades) page. See [Trades](help:trades).

### Delete a card

Click **Delete** at the bottom of the panel and confirm. This removes the card from your collection and can't be undone.

## List a card for sale

From the card details panel:

1. Click **List for sale**.
2. If the entry holds more than one copy, set **Quantity**. Listing fewer than you have splits those copies off into their own entry.
3. Check the **Price**. It starts at the current market price.
4. Choose a **Channel**: Manual, TCGplayer or eBay.
5. Add a **Note** if you like.
6. Click **List for sale**.

![The List for sale dialog with the eBay listing section shown](collection-list-for-sale.png)

If the card is already listed, the button reads **Already listed for sale** and is disabled.

### List on eBay

Choose **eBay** as the channel to publish the card to eBay at the same time. An **eBay listing** section appears with a **Listing title** (up to 80 characters), **Description**, **Condition**, **Listing type** (Fixed price or Auction, with an **Auction duration**), and **eBay category**. OmniCard suggests a title, description and category for you. Click **List on eBay** to publish.

Your eBay account must be connected and set up first. If it isn't, the dialog shows a warning. If the card is listed in OmniCard but eBay rejects it, the error is shown and the local listing is kept so you can fix the problem and try again. See [eBay](help:ebay).

For managing listings, picking and orders, see [Sales](help:sales).

## Work with several cards at once

1. Click **Select**. Checkboxes appear next to each row.
2. Tick the cards you want. The header checkbox selects every card on the current page.
3. Use the buttons that appear in the toolbar. The toolbar shows how many cards are selected.
4. Click **Done** to leave Select mode.

![Select mode with several cards ticked and the bulk action buttons showing](collection-select-actions.png)

A selected stacked row counts every entry in the stack.

| Button | What it does |
|---|---|
| **Move to…** | Moves all selected cards to one location. |
| **Bulk edit** | Changes properties on all selected cards. |
| **List for sale** | Lists all selected cards for sale at their market price. |
| **Export CSV** | Downloads the selected cards as a file. |
| **Delete** | Deletes the selected cards after you confirm. This can't be undone. |

### Bulk edit

In the **Bulk edit** dialog, tick each property you want to change, then set its value. Unticked properties are left alone. You can set **Condition**, **Language**, **Foil** (foil or non-foil), **Quantity**, **Purchase price**, **Note** and **Tags**.

For tags, choose **Add to existing** to add tags, or **Replace all** to overwrite every selected card's tags. Replacing with no tags clears them. Click **Apply to N** to save.

### Bulk list for sale

Each selected card is listed as a whole entry at its current market price. Pick a **Channel**, add an optional **Note**, and click **List for sale**. Cards that are already listed are skipped, and the dialog tells you how many. Adjust individual prices afterwards on **Sales ▸ Listings**.

> [!NOTE]
> Bulk listing records the listings in OmniCard. To publish a card to eBay, list it on its own from the card details panel with the eBay channel.

### Export selected cards

Click **Export CSV** and choose a format:

- OmniCard (full detail)
- TCGplayer
- Moxfield
- ManaBox
- Archidekt
- Deckbox
- Dragon Shield
- Card Price Ticker
- Text list (.txt)

The file downloads to your computer. To bring cards in from a file instead, see [Importing](help:importing).

## Check a decklist against your collection

**Check decklist** shows which cards from a deck you already own, exactly where to find them, and what's missing.

1. Click **Check decklist** at the top right of the page.
2. Choose the **Game**.
3. Paste a Moxfield or Archidekt link into **Decklist URL**, or paste the list into **Decklist text**, one card per line (for example \`4 Lightning Bolt\`).
4. Click **Check**.

![The Check a decklist dialog showing owned and missing counts and the pull list](collection-decklist-check.png)

The results show the deck name and three chips: how many cards you own, how many are missing, and the estimated cost to complete the deck.

### To pull

The **To pull** tab lists each card you own and the copies to take, with set, collector number, condition, a ✦ for foil, and where each copy is (location, section, page and slot).

OmniCard picks copies in this order of preference: copies that aren't listed for sale, then copies that aren't already in another deck box, then the exact printing the deck asks for, then the same set. Copies in locations marked to be ignored for lists are never picked. See [Lists](help:lists).

Chips flag copies that are **Listed for sale** or **In a deck box**.

### Missing

The **Missing** tab lists cards you don't have enough of, with the cost to buy them. The most expensive cards are listed first.

### Print and move

- **Print pull list** downloads a PDF checklist of the cards to pull and where they are.
- **Print missing list** downloads a PDF shopping list of the missing cards with their prices.
- **Move to deck box…** moves every picked copy into a deck box you choose (or create). This is only available once you own every card in the deck and have permission to move cards. If some copies are listed for sale or are in another deck box, a message warns you before you move them.

After a move, the check runs again so the pull list shows the cards' new location. The dialog keeps its results while you close it to look at your collection.

For deck boxes and saved card lists, see [Deck boxes](help:deck-boxes) and [Lists](help:lists).

## Tips and troubleshooting

- **A search finds nothing.** Check the game selector in the top bar. A search only covers the selected game, unless **All Games** is selected.
- **Market is blank.** No price is available for that printing yet. Prices come from the game's price data and update when the card catalog is refreshed.
- **I can't click List for sale or Split stack.** The card is already listed. Unlist it from **Sales ▸ Listings** first.
- **Buttons are greyed out or missing.** You may not have permission for that action, or the card is in a read-only site. Ask an administrator for access.
- To view one location's cards only, open it from [Locations](help:locations). The same card list and search work there.
`,je=`# Dashboard

The Dashboard shows what your whole collection cost, what it's worth today, and how much profit your sales have made. It opens when you first sign in.

## Open the Dashboard

Click **Dashboard** at the top of the sidebar, or go to [the Dashboard](/). If you don't see **Dashboard** in the sidebar, ask an administrator for access.

![The Dashboard with the five totals and the By Game and By Category tables](dashboard-overview.png)

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

## How the numbers are worked out

- **Everything counts.** The Dashboard covers all games, all sites and all locations. Choosing a game in the top bar doesn't change it.
- **Traded-away cards are left out** once a trade is finalized. See [Trades](help:trades).
- **Single cards** are valued at their current market price times their quantity. Foil and non-foil copies use their own prices.
- **Sealed product** is valued at the **Market** price shown for it on the Inventory page. Product with no market price counts as zero. See [Inventory](help:inventory).
- **Missing purchase prices count as zero.** If you didn't enter what you paid, **Cost** is understated and **Unrealized** looks bigger than it really is. You can add purchase prices when you scan, or edit them later in the [Collection](help:collection).
- **Missing market prices count as zero.** Some cards have no price from their game's price source, so **Market** may be a little low. See [Troubleshooting](help:troubleshooting).
- **Realized Profit** is based on the cards and items you've sold. When only part of a stack was sold, only the cost of the sold copies is counted. Marketplace fees come from shipped and completed orders. See [Sales](help:sales).

> [!TIP]
> Market prices are only as fresh as the last price update. An administrator can refresh them under **Administration ▸ Catalog Data** with **Update prices**. See [Administration](help:administration).

## Related topics

- [Collection](help:collection): edit purchase prices and see each card's market price
- [Locations](help:locations): the value of each binder, box or deck
- [Sales](help:sales): orders and listings that feed **Realized Profit**
- [Inventory](help:inventory): sealed product and its market prices
`,Ge=`# Deck boxes

A deck box is a location that holds one built deck. It belongs to a single game, can be given a deck type (format) such as Commander or Standard, and warns you when the deck doesn't meet that format's rules.

## What makes a deck box different

Deck boxes are ordinary [locations](help:locations) with a few extras:

- **One game only.** Every deck box is assigned a game, and cards from other games can't be put in it.
- **Deck type.** You can pick a format for the deck. OmniCard then checks the deck's size, copy limits and commander against that format.
- **Deck panel.** The deck box's page shows the game, deck type, card count and any legality warnings.
- **Stacked view.** The deck can be shown as overlapping card stacks grouped by type, like a deck-building site.

## Create a deck box

1. Go to [Locations](/locations).
2. Type a name in **New location name**.
3. Set **Type** to **Deck Box**.
4. Choose a **Game**. This is required for a deck box.
5. Optionally choose a **Deck type**, or leave it as **None**. The list only shows formats for the game you picked.
6. Click **Add**.

![Creating a deck box with a game and deck type](locations-add-bar.png)

You can also create a deck box from any "move to location" picker by clicking **New location**.

## Set the game and deck type

To change a deck box's game or format later, do either of the following:

- On the [Locations](/locations) page, open the deck box row's **⋮** menu and choose **Game & deck type…**.
- On the deck box's own page, click **Game & deck type** in the deck panel.

Choose the **Game** and **Deck type**, then click **Save**.

You can't switch a deck box to a game that doesn't match the cards already in it. Move those cards out first.

### Deck boxes with no game

Deck boxes created before games were required may not have one. When that happens, the Locations page shows a warning such as *Deck box "Pauper Elves" has no game assigned.*

1. Click **Assign game** in the warning.
2. OmniCard suggests a game based on the cards inside. Check it, pick a **Deck type** if you like, and click **Save**.
3. Repeat for each deck box listed. The warning disappears once every deck box has a game.

## One game per deck box

Once a deck box has a game, OmniCard keeps other games out of it:

- **Add card** on the deck box's page is locked to the deck box's game (*Locked to this deck box's game*).
- In "move to location" pickers, deck boxes for a different game are greyed out, with the note *This deck box only holds … cards*.
- Any other attempt to move a card from another game into the deck box is refused.

The game selector at the top of the app also hides deck boxes that belong to other games on the Locations page. Choose **All Games** to see them all.

## The deck panel

Open a deck box from the Locations page to see its panel above the card list.

![A deck box page with its deck panel and legality warnings](deck-boxes-panel.png)

The panel shows:

- The game. If none is assigned, you'll see **No game assigned** instead.
- The deck type, if one is set.
- How many commanders the deck has, when it has any.
- The total card count, for example *100 cards (99 + commander)*.
- The **Game & deck type** button.
- Any legality warnings, under **Deck legality (*deck type*)**.

## Legality warnings

When a deck box has a deck type, OmniCard checks the cards in it against that format's rules and lists anything that doesn't fit. Warnings are advice only. They never stop you from adding, moving or removing cards.

OmniCard checks:

| Rule | Example warning |
|---|---|
| Minimum deck size | Deck has 58 cards; Standard needs at least 60. |
| Maximum deck size | Deck has 101 cards; Commander allows at most 100. |
| Copies per card | "Lightning Bolt" appears 5× — Modern allows at most 4. |
| Singleton formats | "Sol Ring" appears 2× — Commander is singleton (max 1). |
| Commander or leader | Commander needs a commander/leader — tag at least one card "commander". |

### Mark commanders and sideboard cards with tags

OmniCard uses two special tags to understand your deck. Add them in a card's details, in the **Tags** field (see [Collection](help:collection)):

- \`commander\`: the card is a commander, leader or other command-zone card. Commanders count toward the deck size, are shown in their own **Commander** group in the stacked view, and satisfy the "needs a commander" rule. A deck can have more than one (for example partners).
- \`sideboard\`: the card is in the sideboard. Sideboard cards are left out of the deck-size and copy-limit checks.

### Built-in deck types

| Game | Deck types |
|---|---|
| Magic: The Gathering | Commander, Standard, Pioneer, Modern, Legacy, Vintage, Pauper, Brawl, Oathbreaker, Limited / Draft, Cube |
| One Piece | Constructed |
| Riftbound | Constructed |
| Pokémon | Standard, Expanded, Unlimited |
| Yu-Gi-Oh! | Advanced, Traditional |
| Final Fantasy TCG | Standard, Classic |

A few things to know about the built-in rules:

- In Commander, Brawl and Oathbreaker, basic lands are exempt from the singleton rule.
- In One Piece, copies are counted by card number, so alternate arts of the same number count as the same card.
- The checks cover deck size, copy limits and commanders only. They don't check banned lists or card legality in a format.

### Customize deck types

Administrators, and anyone given access, can edit the built-in deck types and add new ones under **Administration ▸ Deck Types**. Pick a game, then click **Add deck type** or edit an existing one. Each deck type can set:

- **Deck size min** and **Deck size max**
- **Max copies / card**, or **Singleton (max 1 of each card)**
- **Commander/leader slots**
- **Basic lands exempt from copy limit**
- **Count copies by card number (all arts of a number count as one card)**

Deleting a deck type removes it from any deck boxes using it. See [Administration](help:administration).

## Stacked view

The stacked view shows a deck the way deck-building sites do: one column per group, with the cards overlapping so you can see the whole deck at once.

1. Open the deck box (or any location).
2. Click the **Stacked view** button (the columns icon) to the right of the search box. Click the **Table view** button (the list icon) to switch back. OmniCard remembers your choice.

![A deck in stacked view grouped by type](deck-boxes-stacked-view.webp)

### Group by

Use **Group by** above the stacks to choose how cards are grouped:

- **Type**: a **Commander** group first (cards tagged \`commander\`), then one group per card type. Magic decks use Creature, Planeswalker, Battle, Instant, Sorcery, Artifact, Enchantment and Land, in that order. Other games use their own main types, such as Pokémon, Trainer and Energy, or Monster, Spell and Trap. A card with several types goes in its main group; an Artifact Creature is grouped with Creatures.
- **Tags**: one group per tag, in alphabetical order, plus an **Untagged** group at the end. A card with several tags appears in each of their groups, so group counts can add up to more than the deck total.

Each group heading shows its number of copies. The line above the stacks shows the deck total and number of groups, for example *60 cards · 8 groups*.

### Look at cards in a stack

- Hover over a card to bring it to the front. The cards below it slide down so you can see it in full.
- Click a card to open its details. It stays expanded while the details are open.
- Use the search box above the view to narrow the cards shown. See [Search syntax](help:search-syntax).

## Build a deck box from a decklist

If you own every card in a decklist, you can pull them all into a deck box in one step:

1. On the [Collection](/collection) page, click **Check decklist**.
2. Paste a Moxfield or Archidekt URL, or paste the decklist text, and check it.
3. When you own the whole deck, click **Move to deck box…** and choose the deck box.

See [Collection](help:collection) for the full decklist check. To bring a deck's cards in as new cards instead, use **Import** on the deck box's page with a deck URL. See [Locations](help:locations#import-into-a-location).

## Troubleshooting

- **A deck box is missing from the Locations page.** The game selector is set to a different game. Choose that game or **All Games**.
- **I can't move a card into a deck box.** The card is from a different game than the deck box.
- **Saving a new game for a deck box shows an error.** The deck box already holds cards from another game. Move them out first.
- **There are no legality warnings.** Either the deck meets the format's rules, or the deck box has no deck type. Set one with **Game & deck type**.
- **The deck says it needs a commander.** Tag your commander card \`commander\` in its details.
`,Ye=`# eBay

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
- **Address line 1**, **City**, **Postal code** and **Country (ISO-2, e.g. US)** are required. Use a two-letter country code, such as \`US\`.
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
`,He=`# Getting started

OmniCard helps you scan, organize, value and sell your trading card collection from any browser. This topic covers signing in, finding your way around the screen, the key ideas behind OmniCard, and a walkthrough for your first 30 minutes.

## What OmniCard does

OmniCard keeps track of every card you own and where it physically lives. You can:

- **Scan** cards from photos, a phone camera, a webcam, or a document scanner. OmniCard recognizes each card and its printing for you.
- **Organize** cards into locations such as binders, boxes, deck boxes and display cases, grouped under sites (for example, your home and your shop).
- **Browse and search** your whole collection, with live market prices.
- **Check sets and decklists** to see what you own and what you're missing.
- **Track sealed product** (booster boxes, packs, decks) on the Inventory page.
- **Trade and sell** cards, including order tracking, receipts and eBay listings.

OmniCard supports these games:

| Game | Notes |
|---|---|
| Magic: The Gathering | Also downloads non-English printings if an administrator turns them on |
| One Piece TCG | English, Japanese and French printings available |
| Pokémon | English, plus Japanese sets with their own prices |
| Yu-Gi-Oh! | English catalog |
| Final Fantasy TCG | English catalog |
| Riftbound | English catalog |

## Sign in

OmniCard opens on a sign-in screen.

1. Enter your **Username** and **Password**.
2. Tick **Remember me** if you're on your own device and want to stay signed in for up to 30 days. Leave it unticked on a shared computer. You'll then be signed out when you close the browser.
3. Click **Sign in**.

![The OmniCard sign-in screen](getting-started-sign-in.png)

If you see *Incorrect username or password*, check your typing and try again. An administrator creates accounts and can reset your password. See [Sign-in problems](help:troubleshooting).

> [!WARNING]
> A brand-new OmniCard server comes with a built-in **Admin** account whose password is \`admin\`. If you're setting OmniCard up, sign in with it and change that password right away under **Administration ▸ Users**.

## Find your way around

![The main OmniCard screen with the sidebar, top bar and Collection page](getting-started-layout.png)

### Top bar

- **OmniCard** on the left is the app name.
- The **game selector** filters most pages to one game. Choose **All Games** to see everything at once. Your choice is remembered in this browser.
- The **Account** button (the person icon) opens your account menu. It shows **Signed in as** with your username, and has **Sign out**. Administrators also see **Account & users**, a shortcut to user management.

### Sidebar

The sidebar on the left lists the sections you have access to. You might not see all of them. Which ones appear depends on the permissions an administrator gave you.

| Section | What it's for | Learn more |
|---|---|---|
| **Dashboard** | Totals for your collection's cost, market value and profit | [Dashboard](help:dashboard) |
| **Scan** | Photograph or upload cards and add them to a location | [Scanning cards](help:scanning) |
| **Collection** | Search, edit, move, export and sell cards you own | [Collection](help:collection) |
| **Locations** | Binders, boxes, deck boxes and display cases | [Locations](help:locations) |
| **Sets** | Set checklists showing what you own | [Sets](help:sets) |
| **Inventory** | Sealed product such as boxes, packs and decks | [Inventory](help:inventory) |
| **Lists** | Want lists and decklists checked against your collection | [Lists](help:lists) |
| **Trades** | Build and record trades | [Trades](help:trades) |
| **Import** | Bring in cards from CSV files and deck sites, or export them | [Importing](help:importing) |
| **Sales** | Orders, customers and listings | [Sales](help:sales) |
| **Administration** | Your password, plus settings and user management | [Administration](help:administration) |
| **Help** | This help center, at the bottom of the sidebar | |

A number on the **Scan** icon counts scan batches that are waiting for someone to review them. See [Scan batches](help:scan-batches).

### On a phone

On a narrow screen the sidebar is hidden to save space.

1. Tap the **menu button** (three lines) at the top left to open the sidebar.
2. Tap a section. The sidebar closes again on its own.

The game selector and the account menu stay in the top bar. Most pages work well on a phone. For scanning on a phone, see [Scanning cards](help:scanning).

![OmniCard on a phone with the sidebar opened from the menu button](getting-started-phone-menu.png)


### Using Help

Click **Help** at the bottom of the sidebar at any time. The help home groups every topic by area; type in **Search help** to find topics that mention a word, such as \`binder\` or \`condition\`. Inside a topic, the list on the left jumps between topics, **On this page** (on wide screens) jumps between sections, and **Previous** / **Next** at the bottom walk through the guides in order.

![The Help home page with topic search](help-home.png)

## Key concepts

### Sites, locations and cards

OmniCard organizes your collection in three levels: **Site ▸ Location ▸ Card**.

- A **site** is a major physical place, such as a home, a shop or a storage unit. Every OmniCard has a **Default** site that everyone can see. Administrators can add more sites, and some people may only see some of them.
- A **location** is where cards physically live inside a site. Location types are **Binder**, **Box**, **Deck Box** and **Display Case**. There's also **Bulk** for cards that aren't sorted anywhere special.
- A **card** is a copy you own, kept in one location.

See [Locations](help:locations), [Binders](help:binders) and [Deck boxes](help:deck-boxes).

### Stacks and quantity

Identical copies in the same location can be kept together as one entry with a **Quantity** greater than 1. This is called a stack. You can split a stack later, for example so each copy gets its own binder pocket. See [Collection](help:collection).

### Card details

Each card you own records:

- **Condition** using the usual grades: **NM** (near mint), **LP** (lightly played), **MP** (moderately played), **HP** (heavily played) and **DMG** (damaged).
- **Foil** for foil copies. Some games also record the foil type.
- **Language**, which is English unless you choose another. OmniCard can read the printed language on some cards when you scan them.
- **Purchase price**, used to work out your cost and profit.
- **Tags** and a **Note** for anything else, for example "signed" or "misprint".

### Prices

Market prices come from each game's price source and are refreshed by an administrator. Some cards have no market price. See [Missing prices or images](help:troubleshooting).

### Permissions

What you can see and do depends on your account:

- **Administrators** can see and do everything.
- Everyone else gets a **role**, such as *Viewer* (look but don't change) or *Staff* (everyday work, without deleting or changing settings). An administrator can also give or remove individual permissions.
- **Site access** decides which sites you can see, and whether you can only view them or also change them.

If a section, button or site is missing, ask an administrator for access. See [Administration](help:administration).

## Your first 30 minutes

This walkthrough takes you from a fresh setup to a searchable collection. Steps 1 and 2 need an administrator. If someone has already set OmniCard up, skip ahead to step 3.

### Step 1: Download catalog data (administrator)

OmniCard recognizes cards by comparing them with each game's card catalog. The catalog needs to be downloaded before you scan.

1. Go to **Administration ▸ Catalog Data**.
2. Pick a **Game**.
3. Click **Download catalog** and wait for it to finish. Progress shows below the buttons.
4. Click **Update prices** to get market prices.
5. Optionally, click **Download artwork** so card images load from your own server.
6. Repeat for each game you collect.

Only one job runs at a time. A big catalog such as Magic: The Gathering can take a while. See [Administration](help:administration).

### Step 2: Add users (administrator)

If other people will use OmniCard, add an account for each of them under **Administration ▸ Users**, and give each one a role. If you have more than one physical place, set up **Sites** too. See [Administration](help:administration).

### Step 3: Create your locations

1. Open [Locations](/locations).
2. Type a **New location name**, for example "Red binder".
3. Choose a **Type**: **Binder**, **Box**, **Deck Box** or **Display Case**. A deck box also needs a game.
4. Click **Add**.

Create a location for each binder, box or deck you want to track. See [Locations](help:locations).

### Step 4: Get cards into OmniCard

Pick whichever way suits you:

- **Scan them.** Open [Scan](/scan), choose the **Game**, then click **Take photo**, **Use webcam** or **Add images**. Review the matches and fix any that are wrong with **Search catalog**. Click **Add to location…** to pick where they go, click **Confirm checked** to accept the matches, then click **Add confirmed cards**. See [Scanning cards](help:scanning).
- **Import them.** If you already track your cards in TCGplayer, Moxfield or ManaBox, export a CSV file there and bring it in on [Import](/import). You can also import a public Moxfield or Archidekt deck straight from its link. See [Importing](help:importing).
- **Add them one at a time.** Open a location and use **Add card**. See [Locations](help:locations).

> [!TIP]
> If you have a document scanner, an administrator can set up a watched folder. Scans saved there are matched in the background and wait for you on the Scan page. See [Scan batches](help:scan-batches).

### Step 5: Browse your collection

1. Open [Collection](/collection).
2. Type in the search box to find cards. Plain text searches by name. You can also use search terms such as \`t:creature\`. See [Search syntax](help:search-syntax).
3. Click a card to change its condition, language, quantity, price, location or tags.
4. Check the [Dashboard](/) to see what your collection is worth.

## Where to go next

- [Scanning cards](help:scanning): every scanning option, corrections and badges
- [Scan batches](help:scan-batches): reviewing scans from a watched scanner folder
- [Auditing a location](help:location-audit): rescan a binder or box to check it's accurate
- [Collection](help:collection) and [Search syntax](help:search-syntax): finding and editing cards
- [Sets](help:sets): set checklists and want lists
- [Locations](help:locations), [Binders](help:binders) and [Deck boxes](help:deck-boxes): organizing your cards
- [Lists](help:lists): decklists and want lists checked against what you own
- [Importing](help:importing): CSV files and deck links
- [Trades](help:trades): recording trades
- [Inventory](help:inventory): sealed product
- [Sales](help:sales) and [eBay](help:ebay): selling cards
- [Dashboard](help:dashboard): collection value and profit
- [Administration](help:administration): settings, users, sites and catalog data
- [Troubleshooting](help:troubleshooting): answers to common problems
`,ze=`# Importing and exporting

Bring cards into OmniCard from a CSV file or a Moxfield or Archidekt deck, and export your collection to CSV for other apps. Everything is on the Import / Export page.

## What this page is for

The **Import / Export** page has three parts:

- **Import collection (CSV)**: add cards from a collection spreadsheet that OmniCard or another app exported.
- **Import from Moxfield / Archidekt**: add every card in a public deck to a location, as cards you own.
- **Export collection (CSV)**: download your collection in a format another app can read.

Open it from **Import** in the navigation menu, or go to [Import](/import).

![The Import / Export page with the CSV import, deck URL import, and export sections](importing-page.png)

> [!NOTE]
> If you don't see **Import** in the menu, ask an administrator for access. You can import only into locations at sites you're allowed to change.

## Import a CSV file

1. Open [Import](/import).
2. Under **Import collection (CSV)**, click **Choose CSV file** and pick your file. The button changes to show the file's name.
3. Leave **Skip duplicates already in collection** ticked to avoid adding cards you already have. See **Duplicates** below.
4. Optionally, choose a **Target location (optional)** for the cards. If you leave it on **— none —**, the cards are added without a location.
5. Click **Import**.

When the import finishes, a message shows how many rows were imported, out of how many, and which format OmniCard detected. Rows that couldn't be read are skipped and counted as warnings.

### Supported CSV formats

OmniCard reads the file's column headings to tell the format apart. You don't need to choose a format.

| Format | Where it comes from | Games |
|---|---|---|
| App-native | OmniCard's own export | All games |
| TCGplayer | TCGplayer collection export | Magic: The Gathering |
| Moxfield | Moxfield collection export | Magic: The Gathering |
| ManaBox | ManaBox collection export | Magic: The Gathering |

If OmniCard doesn't recognize the file, nothing is imported. Export the file again from the other app, and don't edit its column headings.

### How rows are read

- **Quantity**: a blank quantity counts as 1.
- **Condition**: codes like NM, LP, MP, HP, and DMG work, and so do full names like *Near Mint* or *Lightly Played*, and ManaBox spellings like *near_mint*. A blank or unrecognized condition is imported as NM.
- **Foil**: foil cards get the game's standard foil finish unless the file names a finish.
- **Language**: a *Language* column is read if the file has one. Codes like \`ja\` and names like *Japanese* both work. A blank or unrecognized language is imported as English.
- **Purchase price**: kept when the file includes it.

### Duplicates

With **Skip duplicates already in collection** ticked, OmniCard skips a row when you already own a copy of the same printing, with the same finish, condition, and language, anywhere in your collection. It skips the whole row, not just the extra copies. For example, if you own one copy of a card and the file lists four, none of the four are added.

Untick the option to add every row as new cards, even when you already own matching copies. That's useful when a file lists new copies of cards you already have.

> [!WARNING]
> If you import the same file twice with **Skip duplicates already in collection** unticked, every card is added twice.

## Import a deck from Moxfield or Archidekt

This adds every card in a public Moxfield or Archidekt deck to one location, as cards you own. Use it when you've built a deck in real life and want it in your collection. These sites are for Magic: The Gathering only.

1. Under **Import from Moxfield / Archidekt**, paste the deck's address into **Deck URL**.
2. Choose a **Target location**. This is required.
3. Choose the **Condition** for all the cards (NM by default).
4. Optionally, tick **Skip duplicates already in collection**. It's unticked by default for deck imports.
5. Click **Import**.

OmniCard keeps each card's exact printing and its foil or etched finish from the deck. When a deck lists the same card more than once, the copies are combined into one stack.

The result message shows:

- How many cards were imported, out of how many in the deck.
- How many were skipped as duplicates, if you ticked that option.
- Cards whose exact printing wasn't in the catalog. OmniCard used the cheapest printing of the same card instead, so check these later.
- Cards that couldn't be found at all. Add these by hand.

![A finished deck URL import showing substituted and unmatched cards](importing-url-result.png)

> [!TIP]
> If you want to track a deck without adding the cards to your collection, use [Lists](help:lists) instead. Lists show what you own and what you need to buy.

## Export your collection

1. Choose a game with the game selector at the top of the app, or choose **All Games** to export everything.
2. Under **Export collection (CSV)**, click a format. The file downloads right away.

| Format | Use it for |
|---|---|
| **App-native** | A full OmniCard backup, or moving cards between OmniCard installs |
| **TCGplayer** | TCGplayer |
| **Moxfield** | Moxfield (Magic only) |
| **Manabox** | ManaBox (Magic only) |
| **Archidekt** | Archidekt (Magic only) |
| **Deckbox** | Deckbox (Magic only) |
| **Dragon Shield** | Dragon Shield card manager |
| **Text list** | A plain list, one line per card, that you can paste into most deck builders |

## Other ways to import

- **Into one location, all or nothing**: open a location and click **Import**. You can use a CSV file or a deck URL. Every card goes into that location, and if any line has a problem, nothing is imported and each problem is listed so you can fix it. See [Locations](help:locations).
- **Checking a decklist**: to see which cards in a decklist you own and which are missing, without importing anything, use **Check decklist** on the Collection page. See [Collection](help:collection).
- **Saving a deck to build later**: import its URL on the Lists page. See [Lists](help:lists).
- **Sales orders**: order CSV files, such as TCGplayer order exports, are imported from **Sales ▸ Orders** with **Import CSV**. See [Sales](help:sales).
- **Scanning cards**: to add physical cards with a scanner or camera, see [Scanning](help:scanning).

## Troubleshooting

- **"No importable rows found."**: the file's column headings don't match a supported format, or no row could be read. Export it again from the original app.
- **Fewer cards than expected**: **Skip duplicates already in collection** may have skipped rows you already own. Import again with it unticked, but only for the rows you need, or you'll add copies twice.
- **"Couldn't fetch that deck URL"**: make sure the deck is public, and that the address is a Moxfield or Archidekt deck page.
- **A card shows the wrong printing after a deck import**: its exact printing probably wasn't in the catalog, so the cheapest printing was used. The result message lists these cards. To fix one, see [Collection](help:collection).

For other problems, see [Troubleshooting](help:troubleshooting).
`,Ve=`# Sealed inventory

Track sealed product such as booster boxes, packs, decks and bundles: what you have, what you paid, what it's worth, and where it's stored.

## What the Inventory page is for

The **Inventory** page (titled **Sealed Inventory**) holds unopened product. Individual cards (singles) don't live here. They're in your [collection](help:collection).

Inventory is organized in two levels:

- A **product** is the thing itself, such as "Bloomburrow Play Booster Box". It has a game, a type, set details, a UPC and a market price.
- A **lot** is a batch of that product you own: how many units, what each one cost, where they're stored and where they came from. One product can have many lots, for example a box bought at release and two more bought later at a different price.

Open it from the **Inventory** item in the navigation menu, or go to [Inventory](/inventory).

![The Sealed Inventory page with value totals and the product list](inventory-page.png)

> [!NOTE]
> If you don't see **Inventory** in the menu, ask an administrator for access.

## Read the page

At the top, three totals summarize all your sealed inventory:

| Total | What it shows |
|---|---|
| **Total Units** | How many sealed items you own, across every lot |
| **Cost** | What you paid, based on each lot's unit cost |
| **Market** | What the product is worth at its market price |

Below the totals, the product list shows one row per product:

| Column | Meaning |
|---|---|
| **Product** | The product name |
| **Type** | Case, Box, Pack, Deck, Bundle or Other |
| **Set** | The set code |
| **Qty** | Total units across all of the product's lots |
| **Market** | The market price of one unit |
| **UPC** | The product's barcode number, if you entered one |

Click a column header to sort by it.

### Filter by game

The product list follows the game selector at the top of the app. Pick a game to see only that game's products, or choose **All Games** to see everything. The three totals always cover your whole inventory.

## Add a new product

1. Click **New product**.
2. In **New sealed product**, enter a **Name**. It's the only required field.
3. Choose the **Game** and the **Type** (Case, Box, Pack, Deck, Bundle or Other). The game starts as the one picked in the game selector.
4. Optionally fill in **Set name**, **Set code**, **UPC** and **Market price**.
5. Click **Save**.

![The New sealed product dialog](inventory-new-product.png)

A new product has no units yet. Add a lot to record what you own.

> [!TIP]
> Enter the UPC from the box's barcode. It shows in the **UPC** column, so you can find a product by the number printed on the package.

## Add units you own (lots)

1. Click a product in the list. Its details panel opens on the right.
2. In the **Lots** section, click **Add lot**.
3. Fill in the lot:
   - **Quantity**: how many units are in this lot (at least 1).
   - **Unit cost**: what you paid for each one.
   - **Location (optional)**: where the product is stored. Choose from your [storage locations](help:locations).
   - **Source (optional)**: where you got it, such as a store name or "Preorder".
4. Click **Save**.

![A product's details panel with its lots](inventory-product-drawer.png)

The product's **Qty** and the page totals update right away.

> [!TIP]
> Bought the same product at different prices? Add a separate lot for each purchase. Your **Cost** total stays accurate, and each lot keeps its own source.

## Edit, move or remove a lot

In the product's details panel, each lot shows its **Qty**, **Cost** and **Source**, with buttons on the right.

- **Edit a lot**: click the pencil button, change the fields and click **Save**. Lower the **Quantity** after you open or sell some units.
- **Move a lot**: click the pencil button and pick a different **Location (optional)**. Choose **— none —** if it isn't stored anywhere in particular.
- **Delete a lot**: click the trash button and confirm. This removes the lot and its units for good.

## Edit or delete a product

At the top of a product's details panel:

- Click the pencil button (**Edit product**) to change the name, game, type, set details, UPC or market price.
- Click the trash button (**Delete product**) to delete the product. You're asked to confirm, because **this also deletes all of its lots**.

### Keep market prices current

The **Market price** of a sealed product is a value you enter. To keep the **Market** total meaningful, open **Edit product** and update the price now and then.

## Tips

- Use specific product names, including the set and product type (for example "Duskmourn Collector Booster Box"), so similar products are easy to tell apart.
- Use lots to tell purchases apart, and products to tell different items apart.
- To store sealed product next to your cards, create a location for it on the [Locations](help:locations) page, then choose it on each lot.
- Selling cards rather than sealed product? See [Sales](help:sales) and [eBay](help:ebay).
`,qe=`# Lists

Lists are saved card lists, such as a deck you want to build or cards you plan to buy. OmniCard shows which cards you already own and where they are, then helps you pull, buy, and put them away.

## What lists are for

A list is a set of cards for one game, each with a quantity. A list never changes your collection by itself. It's a plan. For every card on the list, OmniCard checks your collection and shows how many copies you already own. It also shows what the rest would cost to buy.

When the deck comes together, use **Put cards away**. OmniCard moves the copies you own into the deck's location, and it adds the copies you bought as new cards. You don't have to do any of this card by card.

Open lists from **Lists** in the navigation menu, or go to [Lists](/lists).

![The Lists page with a list selected, showing owned counts and the Put cards away panel](lists-overview.png)

> [!NOTE]
> If you don't see **Lists** in the menu, or some buttons are missing, ask an administrator for access.

## Create a list

1. Open [Lists](/lists).
2. Pick the **Game** at the top left. Each list belongs to one game, and the page shows only lists for the game you pick.
3. Type a name in **New list name**.
4. Click **Create**.

The new list appears as a chip under the top panel and opens right away. Each chip shows the list's name and how many cards it holds. Click a chip to open that list.

### Rename or delete a list

- To rename the open list, click **Rename** above it and type the new name.
- To delete a list, click the delete icon on its chip and confirm. Deleting a list doesn't change your collection.

## Import a list from Moxfield or Archidekt

You can turn a public Moxfield or Archidekt deck into a new list.

1. Open [Lists](/lists) and pick the **Game**.
2. Under **or import from a URL**, paste the deck's address into **Moxfield / Archidekt deck URL**.
3. Optionally, choose a card language next to it. Leave it on **Any language** to count copies in any language. See [Set the card language](#set-the-card-language).
4. Click **Import as new list**, or press Enter.

OmniCard names the list after the deck and opens it. A message tells you how many cards were added. If some cards couldn't be matched to the catalog, they're named in the message so you can add them by hand.

The list remembers the deck's address, so you can update it later when the deck changes. See [Update a list from its URL](#update-a-list-from-its-url).

### Add a deck to an existing list

To add a whole deck to the list that's already open, paste its address into **Add from URL (Moxfield / Archidekt)** in the list, then click **Add**.

## Add cards by hand

1. Open the list and click **Add card**.
2. In the **Add card to list** dialog, check the **Game**, then search by **Name**. You can narrow the search with **Set** and **Collector #**.
3. Results come in two groups:
   - **In your collection**: copies you already own. Each shows its set, number, condition, and location. Click one to add that copy to the list. The copy doesn't move yet. When you put the list away, that copy is the one that gets moved.
   - **In the catalog**: every printing in the game's catalog, including ones you don't own. Click one, set the **Qty** and **Foil**, then click **Add to list**.
4. The dialog stays open so you can add several cards in a row. The title shows how many you've added so far. Click **Close** when you're done.

![The Add card to list dialog with results from your collection and the catalog](lists-add-card-dialog.png)

## Read a list

The open list shows a table of its cards:

| Column | What it shows |
|---|---|
| (icon) | Whether you own the card. See the icons below. |
| **Card** | The card name. ✦ means foil. Hover over the name to see the card image. |
| **Set** | The set code and collector number. |
| **Qty** | How many the list needs. Type a new number to change it. |
| **Owned** | How many copies of this exact printing you own, up to the quantity needed. |
| **Price** | The current market price for one copy. A dash means no price is known. |

Click the delete icon at the end of a row to take that card off the list.

### Icons in the first column

- **Collection icon**: you own this printing. It's green when you own enough copies and orange when you own some but not all.
- **Swap icon**: a stand-in, meaning another printing from your collection is filling in for the card. See [Use other printings you own](#use-other-printings-you-own).
- **Shopping cart**: awaiting purchase. Your copies were already moved and the rest still need to be bought. See [Awaiting purchase](#put-cards-away).

A grey **+N** next to the owned count means you have N more copies, but they're in an ignored location or listed for sale, so the list doesn't count them. See [Ignored locations](#ignored-locations).

### What counts as owned

A copy counts as owned only when it's the same printing and the same finish (foil or not), in a site you can see. A copy of the same printing in another language also counts, unless the list is set to one language. Copies that are listed for sale, sitting in an ignored location, flagged missing, or traded away don't count.

### Value totals

Above the table, **Total market value** is the cost of every card on the list. **To buy** is the cost of only the copies you don't own. Click **Refresh prices** to get the latest market prices.

## Update a list from its URL

When a Moxfield or Archidekt deck changes, you can bring those changes into your list. Nothing changes until you approve it.

1. Open the list and click **Update from URL**. Hover over the button to see the address the list came from.
2. If the list remembers its deck address, OmniCard checks it right away. Otherwise, paste an address into **Moxfield / Archidekt deck URL** and click **Check for changes**.
3. Review the changes. The summary shows how many cards changed and how many didn't. Each change is marked:
   - **Added**: the card is new in the deck. If you already own copies, a note says *you own N*.
   - **Removed**: the card is no longer in the deck.
   - **Quantity**: the deck has a different count, shown as old → new.
4. Tick the changes you want. All changes start ticked except removals of cards marked **(not from the URL)**. Those are cards you added by hand or stand-ins, so OmniCard leaves them unticked to keep them.
5. Click **Apply N changes**.

If the list already matches the deck, you'll see **The list already matches the deck.**

![The Update from URL dialog listing added, removed, and quantity changes](lists-update-from-url.png)

## Use other printings you own

Sometimes you don't own the exact printing on the list, but you do own the same card from another set. **Find in collection** lets those copies stand in.

1. Open the list and click **Find in collection**.
2. For each card you're short on, the dialog shows how many you **need**. Below that are the other printings you own, with their condition, location (including page and slot for binders), and how many are **Available**.
3. Each **Use** box starts at a suggested amount. Change the amounts as you like. You can't use more copies than a card needs, or use the same copies for two cards. If you try, OmniCard highlights the problem.
4. Click **Use N copies**.

The copies you chose become stand-ins on the list and get the swap icon. When you put the list away, those exact copies are moved.

Greyed-out copies are in ignored locations or listed for sale, so you can't use them. The tag on each one says why: **Ignored location** or **Listed for sale**. To change which locations are ignored, click **Ignored locations…** at the bottom of the dialog.

If the list has a card language set, only copies in that language are offered.

![The Find in collection dialog offering other printings as stand-ins](lists-find-in-collection.png)

## Set the card language

Use **Card language** at the top right of an open list to make the list language-specific, for example a Japanese-only deck.

- **Any language** (the default): copies in any language count as owned, and new cards are added in English.
- A specific language: only copies in that language count as owned, and new cards are added in that language.

You can also choose the language when you import a list from a URL.

## Ignored locations

Some cards shouldn't be pulled into a list, like your sales binder or a deck you're playing. Ignore their locations, and lists won't count those cards or take them.

1. In an open list, click **Ignored locations…** in the **Put cards away** panel. You'll also find it in **Find in collection** and on the [Locations](/locations) page.
2. Tick each location to ignore. Locations are grouped by type, and by site if you have more than one site. Tick a group heading to ignore the whole group, or a site heading (marked *whole site*) to ignore everything at that site.
3. Use **Filter locations** to find a location by name.
4. Click **Save N changes**.

Locations in a site you can only read have a lock icon and can't be changed. Ignored locations also apply to decklist checks. For more about locations, see [Locations](help:locations).

## Print a list

Click **Print** in an open list and choose:

- **Print list**: every card on the list.
- **Print pick list**: the copies you own, grouped by where they are, so you can walk from location to location and pull them.
- **Print buy list**: only the copies you still need to buy.

Each one downloads as a PDF.

## Put cards away

When you've collected the cards, the **Put cards away** panel moves everything into place in one step.

1. **Move N owned cards to**: click **Choose location…** and pick where your owned copies go, such as the deck box.
2. **Add N new cards to**: click **Choose location…** and pick where your newly bought copies go. Set **Cond** for the new cards (NM by default).
3. When you pick a location for one row, the other row uses it too, unless you've already set it. Usually both go to the same place.
4. Click one of these:
   - **Move owned**: moves only the copies you own.
   - **Add new**: adds only the missing copies as new cards in your collection.
   - **Move & add**: does both.

Here's what happens:

- Owned copies move to the chosen location. If a copy is part of a larger stack, OmniCard splits off just the copies it needs.
- New cards are added in the list's card language, or in English when the list allows any language.
- Cards that are done come off the list. When every card is done, the list is deleted and a message confirms it.

In the location picker, you can't choose a deck box that's set to a different game. For more about choosing and creating locations, see [Locations](help:locations) and [Deck boxes](help:deck-boxes).

![The Put cards away panel with locations chosen for owned and new cards](lists-put-cards-away.png)

### Awaiting purchase

If you click **Move owned** before you've bought the rest, the cards you moved come off the list. The cards that still need buying stay on the list with a shopping-cart icon. This means *your copies were already moved, the rest is to buy*. Those cards won't count other copies in your collection as owned, so the same copies aren't counted twice. When you've bought the cards, use **Add new** to add them.

## Tips

- Lists also work as shopping lists. Import a deck, click **Print buy list**, and take the PDF to a card shop.
- Before you put a list away, ignore locations you never want to pull from, like a sales binder.
- If a card shows as not owned but you're sure you have it, check its printing, foil, and language. Then try **Find in collection**.
- To check a decklist against your collection without saving it, use **Check decklist** on the Collection page. See [Collection](help:collection).
- To add a whole deck straight into a location as owned cards, see [Importing](help:importing).
- For decks you've already built, see [Deck boxes](help:deck-boxes).
`,$e=`# Auditing a location

An audit checks a location against what is physically in it. You scan every card that is really there, and OmniCard updates the location to match: cards you scanned are kept, cards you didn't scan are removed, and new cards are added.

## When to audit

Use an audit when a location's contents in OmniCard may have drifted from reality, for example:

- A box or binder has been sorted, traded from, or reorganized without updating OmniCard.
- You want to verify a deck box before a trade or sale.
- You are cleaning up after an import and want the scan to be the final word.

If you only want to add cards to a location, use the normal [Scan](help:scanning) page instead. An audit can delete cards.

## Start an audit

1. Open [Locations](/locations) and click the location you want to audit.
2. On the location's page, click **Audit** (next to **Import** and **Add card**).

The audit page opens, titled *Audit: location name*. A banner reminds you that committing makes this scan the source of truth for the location.

![A location page with the Audit button next to Import and Add card](location-audit-button.png)

> [!NOTE]
> The **Audit** button appears only if you are allowed to delete cards and can change the location's site. If you don't see it, ask an administrator for access.

## Scan the location

The audit page works just like the [Scan](help:scanning) page, except that the target location is locked: instead of **Add to location…** you see the location's name with a lock icon.

1. Choose the **Game**, and if you like the **Sets (art fallback)**, **Condition**, **Card language** and **Foil** defaults.
2. Add a picture of every card in the location with **Take photo**, **Use webcam** or **Add images**.
3. Review each match. **Confirm match** the correct ones and use **Search catalog** to fix the wrong ones. See [Review the list](help:scanning#review-the-list) and [Correct a wrong match](help:scanning#correct-a-wrong-match).
4. Set each card's **Condition** and **Foil** to what you see. These overwrite the stored values for matching cards (see below).
5. When every card is confirmed and checked, click **Commit audit (N cards)**.

![The audit page locked to a location, with the Commit audit button in the action bar](location-audit-page.webp)

Only confirmed and checked scans count toward the audit. **Commit audit** stays disabled until every scan has finished matching.

> [!WARNING]
> Any card in the location that isn't among the confirmed, checked scans is **deleted from your collection** when you commit (it isn't moved anywhere else). Before committing, make sure you scanned everything, that every card you want to keep is confirmed and checked, and that no filter is hiding rows: the commit only counts cards visible in the list. Click **Clear** on the filter bar first if you used one.

## What committing does

When you commit, OmniCard compares the scan with the location card by card:

| Result | What happens |
|---|---|
| **Matched** | The card was in the location and you scanned it. It stays, and its condition and foil are updated from the scan. |
| **Not found** | The card was in the location but you didn't scan it. It is deleted from your collection. |
| **Added** | You scanned a card that wasn't recorded in the location. It is added as a new card. |

A few details:

- Cards are compared by printing. A foil and a non-foil copy of the same printing count as the same card, and the scan decides whether it is foil.
- Quantities are compared too. If the location had three copies and you scanned two, one copy is removed. If you scanned four, one is added.
- Sealed products stored in the location are not touched by an audit.
- A deck box set to one game won't accept cards from another game; the commit is refused if you scanned any.

## The audit summary

After the commit, you return to the location's page and the **Audit complete** window opens. It starts with a one-line count, for example *12 matched, 2 added, 3 not found (removed)*, and notes how many matched cards had their condition or foil overwritten.

Below that are three sections you can expand:

- **Matched** — cards that were found.
- **Not found — removed** — cards that were removed because you didn't scan them.
- **Added** — new cards added from the scan.

Each line shows the card name, set and collector number, condition, foil, and quantity. Click **Done** to close the window.

![The Audit complete window with the Matched, Not found and Added sections](location-audit-summary.png)

## Pausing an audit

Your audit scans are saved in this browser as you work, separately for each location and separately from the main Scan page. If you leave the page or refresh it, open **Audit** on the same location again and you'll see *Restored N scans from your last session*. Click **Discard** in that message to start the audit over.

As on the Scan page, the saved scans stay in this browser on this device only, and aren't kept in a private or incognito window.

## Tips

- Audit one location at a time, and keep the cards you have scanned separate from the ones you haven't.
- For a large location, sort by **Confidence** to review the weakest matches first.
- You can **Export** an audit's scans without committing, for example to keep a record of what was in the box. See [Export scans without adding them](help:scanning#export-scans-without-adding-them).
- To find where a card is stored before you audit, use [Collection](help:collection). For more on locations, see [Locations](help:locations).
`,Qe=`# Locations

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

Type in the search box to filter the cards. Plain text matches card names, and you can use the full search syntax, such as \`t:creature\` or \`set:mh3\`. See [Search syntax](help:search-syntax).

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
`,Je=`# Sales

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
`,Ke=`# Scan batches

Scan batches let a scanner drop images into a watched folder on the server, where OmniCard matches them in the background. You then review each batch on the Scan page and add it to a location, just like an interactive scan.

## How scan batches work

With interactive scanning you upload pictures from your browser and wait while they match (see [Scanning cards](help:scanning)). Scan batches turn that around:

1. Your scanner software saves its images into a folder that OmniCard watches. Each game has its own folder.
2. Each subfolder becomes one **batch**, named after the subfolder. Images saved directly in the game's folder (not in a subfolder) go into a batch named after today's date.
3. When no new file has arrived for a short while (the *quiet period*), OmniCard matches the batch's cards in the background. You don't need to have the browser open.
4. The batch appears on the [Scan](/scan) page. Someone opens it, reviews the matches, and adds the cards to a location.

Unlike interactive scans, which live only in your browser, batches are stored on the server. Anyone with access to Scan can see them, and you can stop reviewing and pick up again later, even from a different device.

After a file is picked up, the original is moved into a **_processed** subfolder of the game's folder, so it is never imported twice.

## Set up watched folders (administrators)

An administrator sets this up once in [Administration ▸ Scan Badges](/settings?tab=scan), in the **Watched scan folders** section:

- **Watch folders** turns the feature on or off.
- **Quiet period (seconds)** is how long to wait with no new file before matching starts.
- **Keep images (days)** is how long stored scans of committed or discarded batches are kept before they are deleted.
- For each game, enter the **Folder on the server** and switch it **Active**. A chip shows **Found**, **Folder not found**, or **Can't move files** (OmniCard can see the folder but can't move files out of it).
- For each folder, choose the defaults every scan in it gets: **Sets (art fallback)**, **Condition**, **Card language**, **Foil**, and an optional **Default location** that is pre-selected when someone reviews a batch from that folder.

Click **Save** when you are done. See [Administration](help:administration) for more about settings.

![The Watched scan folders settings with a folder path, its Found status and batch defaults](administration-scan-folders.png)

> [!NOTE]
> The folder must be on (or reachable from) the server that runs OmniCard, not on your own computer, unless they are the same machine.

## Find waiting batches

When batches are waiting, a number appears on the **Scan** item in the navigation menu. It counts open batches that nobody is reviewing yet.

On the [Scan](/scan) page, the **Scan batches** panel lists every open batch, plus batches closed in the last day. Click the panel's header to show or hide it; chips in the header such as *2 ready* and *1 in progress* stay visible even when it is collapsed. The panel is hidden when there are no batches.

Each batch shows:

- Its name, its game, and a status chip:

| Status | Meaning |
|---|---|
| **Waiting for files** | Files are still arriving. Matching starts after the quiet period. |
| **Matching N of M** | Cards are being matched. A progress bar shows how far along it is. |
| **Ready** | Every card has been matched and the batch is ready to review. |
| **Committed** | The batch was added to your collection. |
| **Discarded** | The batch was thrown away. |

- How many cards it holds, how many errors and how many committed cards it has, and when its last file arrived.
- Who is reviewing it: *You're reviewing this* or *In review by* someone else.

![The Scan batches panel with one Ready batch and one batch still matching](scan-batches-panel.png)

## Review a batch

1. In the **Scan batches** panel, click **Open** on the batch. (If you were already reviewing it, the button reads **Continue**.)
2. The batch opens on its own page, titled *Batch: name*. You are now its reviewer, and nobody else can change it while you have it.
3. Review the cards exactly as you would on the Scan page: compare the scan with the matched art, **Confirm match** or **Search catalog** to correct it, set condition, foil, tags and other properties, and use the filters and bulk **Edit**. See [Review the list](help:scanning#review-the-list) and [Correct a wrong match](help:scanning#correct-a-wrong-match).
4. Check that **Add to location…** shows the right location. If the folder has a default location it is already selected; you can change it.
5. Click **Add N confirmed cards**.

The bar at the top of the batch shows the game and the batch's fixed settings (*Sets*, *Condition*, *Language*, *Foil*) taken from the folder. You can't add more pictures to a batch from the browser; save more files into the folder instead.

Your changes are saved to the server as you work. You don't have to finish in one sitting: leave the page and come back later with **Continue**.

![A batch open for review, with the batch settings bar and the review list](scan-batches-review.webp)

### Adding cards while matching is still running

You can add confirmed cards from a batch even while the rest is still matching; new matches appear in the list as they finish. Files that arrive late are added to the same open batch.

### Fixing errors

If some cards couldn't be matched because of an error, a **Retry N errors** button appears in the batch's settings bar. Click it to match those cards again.

### Removing cards

Click **Remove** on a card to take it out of the batch. Its stored image is deleted from the server. The original file stays in the **_processed** subfolder.

### When the batch is finished

Once every card in a batch has been added or removed, the batch closes and you return to the Scan page. It shows as **Committed** (or **Discarded** if nothing was added) in the panel for a day, then disappears from the list.

## One reviewer at a time

Only one person can review a batch at a time, so two people never add the same cards twice.

- **Open** claims the batch for you. Following a link to a batch nobody is reviewing claims it automatically.
- **Release** gives the batch up so someone else can take it. Your saved changes stay with the batch. The reviewer or an administrator can release it.
- If someone else is reviewing a batch, the panel offers **View** instead of **Open**. The batch opens read-only with the message that that person is reviewing it; you can look but not change anything.
- An administrator viewing someone else's batch can click **Take over** to become its reviewer.
- If a batch you opened has no reviewer (for example after you released it), click **Review this batch** to start reviewing it again.
- If someone takes the batch over while you have it open, you'll see a warning that you're no longer reviewing it, and your last changes may not have been saved.

## Discard a batch

To throw a whole batch away, click **Discard** in the panel or at the top of the batch page, then confirm in the **Discard batch?** window. Its unsaved cards and their stored images are deleted. The original files stay in the folder's **_processed** subfolder, so you can move them back out to scan them again.

The reviewer or an administrator can discard a batch. A batch nobody is reviewing can be discarded by anyone who is allowed to add scans to the collection.

> [!WARNING]
> Discarding can't be undone from within OmniCard.

## Tips and troubleshooting

- **A batch never appears.** Ask an administrator to check that **Watch folders** is on, the game's folder is **Active**, and its status chip reads **Found**. Files must be JPEG, PNG or TIFF images.
- **A batch stays on Waiting for files.** Matching starts only after no new file has arrived for the quiet period. If scanning is still going, that's expected.
- **Batches only process while someone is using the site.** On some servers, background work pauses when the site is idle. An administrator can fix this; the settings page includes a hint about it.
- **Start a new batch.** Save into a new subfolder. Files saved into a subfolder whose batch is still open join that batch; once that batch is closed, new files in the same subfolder start a fresh batch, and a number such as *(2)* may be added to its name to tell them apart.
- **Wrong set or condition for the whole batch?** Use bulk **Edit** to change the cards' properties before adding them. To change the defaults for future batches, ask an administrator.
`,Xe=`# Scanning cards

Use the Scan page to identify cards from photos or scanner images, check each match, fix any that are wrong, and add the confirmed cards to a location in your collection.

## What the Scan page does

Open [Scan](/scan) from the navigation menu. You add pictures of your cards (from a scanner, a phone camera, or a webcam), and OmniCard identifies each one against the selected game's card catalog. Every picture becomes a row in the review list. You look over the matches, correct any mistakes, set details like condition and foil, and then add the cards to a location.

Nothing is added to your collection until you click the add button. Until then the scans live only in your browser, so you can take your time.

![The Scan page with a review list on the left and the selected card's details on the right](scanning-overview.webp)

> [!NOTE]
> If you don't see **Scan** in the menu, ask an administrator for access.

## Scan your first cards

1. Open [Scan](/scan).
2. In the top panel, choose the **Game** you are scanning.
3. Optionally set the **Condition**, **Card language** and **Foil** that most of these cards share. Each new scan starts with these values (see [Session defaults](#session-defaults)).
4. Add pictures with **Take photo**, **Use webcam** or **Add images** (see [Ways to add pictures](#ways-to-add-pictures)).
5. Wait for matching to finish. Each row shows a spinner while it is being matched, then a confidence percentage.
6. Select each row and compare **Uploaded scan** with **Matched art**. Click **Confirm match** when it is right, or **Search catalog** to pick the correct card.
7. Click **Add to location…** and choose where the cards are going.
8. Click **Add N confirmed cards**.

Only cards that are both **confirmed** and **checked** are added. Everything else stays in the list.

## Ways to add pictures

| Button | Best for | What happens |
|---|---|---|
| **Take photo** | Phones and tablets | Opens the device's rear camera so you can photograph one card. On a computer it opens a normal file picker instead. |
| **Use webcam** | A computer with a webcam | Opens a live camera view that finds the card and captures it automatically. |
| **Add images** | Scanner output or saved photos | Lets you pick one or many image files at once. JPEG, PNG and TIFF files are supported. |

You can mix all three in one session. New scans appear at the top of the list.

### Scanning with a webcam

1. Click **Use webcam**. The **Scan with webcam** window opens. If your browser asks for permission to use the camera, allow it.
2. If you have more than one camera, choose it from the **Camera** list.
3. Hold a card in front of the camera on a plain, contrasting background. An outline appears around the card when it is found.
4. Keep the card still. The status changes from **Point a card at the camera** to **Hold steady…**, and after about a second the card is captured: the frame flashes and the status reads **Captured ✓ — present the next card**.
5. Take the card away and present the next one. A card left sitting in view is captured only once.
6. To capture the same card again, click **Capture now**.
7. Click **Done** when you are finished.

The window shows how many cards you have captured and thumbnails of the last few. Each capture is straightened and cropped automatically, then sent for matching just like an uploaded image.

![The Scan with webcam window with a card outlined and the captured count below](scanning-webcam.png)

> [!TIP]
> Most browsers only allow webcam access when OmniCard is opened over a secure (https) address or on the same computer that runs it. If the camera won't start, use **Take photo** on a phone or **Add images** instead, or ask your administrator.

## Session defaults

The top panel controls apply to every scan you add **after** you set them:

- **Game** — which game's catalog to match against. Changing the game clears the set and language choices.
- **Sets (art fallback)** — pick one or more sets if you know what you are scanning. Matching is limited to those sets, and so is the catalog search when you correct a card. Leave it on **All sets** to search everything.
- **Condition** — NM, LP, MP, HP or DMG.
- **Card language** — **Auto (detect)** reads the language printed on Magic: The Gathering and Yu-Gi-Oh! cards. For other games, pick the language of the cards you are scanning.
- **Foil** — tick this before scanning foil cards. It marks the new scans as foil, helps matching cope with foil shine, and shows the foil market value.

Changing these later does not change cards already in the list. To change existing cards, edit them individually or in bulk (see [Card properties](#card-properties)).

## How matching works

OmniCard compares each picture with the catalog's card artwork and also reads the printed text on the card, such as the set code and collector number, to pin down the exact printing. A card scanned upside down is usually still recognized.

The colored chip on each row tells you how sure the match is:

| Chip | Meaning |
|---|---|
| Green percentage (50% or more) | A strong match. Usually correct, but still worth a glance. |
| Amber percentage (15% to 49%) | A possible match. Check it carefully. |
| Red percentage (under 15%) | A weak match. Likely wrong. |
| **No match** | Nothing close enough was found. Use **Search catalog**. |
| **Corrected** | You picked the card yourself from the catalog. |
| **Error** | The picture couldn't be processed. Try a clearer image. |

Matched cards are checked automatically; unmatched ones are not. When you confirm or correct a card, OmniCard remembers that identity, so the same card tends to match better next time.

## Review the list

On a wide screen the review list is on the left and the selected card's details are on the right. On a phone the details appear below the list.

Each row shows the scan thumbnail next to the matched artwork, the card name, set and collector number, a one-line summary of its properties (condition, language, foil, quantity, tags, and a note icon), and its badges. A green check mark before the name means you have confirmed it.

Click a row to see it in the detail panel:

- **Uploaded scan** and **Matched art** side by side, so you can compare them.
- **View scan** opens a large version of your picture so you can read small print.
- The card's name, set, collector number and rarity.
- **Confirm match** marks the match as correct (the button then reads **Looks correct**). Confirming also checks the card.
- **Search catalog** opens the correction search.
- **Exclude from commit** / **Include in commit** unchecks or checks the card.
- **Remove** deletes the scan from the list.

![The detail panel comparing the uploaded scan with the matched art, with Confirm match and Search catalog buttons](scanning-detail-panel.webp)

### Checking cards

The checkbox on each row decides whether the card is included when you add or export. Only cards with a match (or a correction) can be checked.

- **Shift**-click a second checkbox to check or uncheck every row between it and the last one you clicked.
- The header above the list has a check-all box plus **All**, **None** and **Invert**, and shows how many are checked.
- **Confirm N checked** in the action bar confirms every checked, matched card at once. Use it after you have eyeballed a batch of strong matches.

## Correct a wrong match

1. Select the card and click **Search catalog**.
2. Type at least two letters of the card's name, or enter its **Collector #**, or both.
3. Hover over a result's small picture to see it larger.
4. Click the correct result.

The card is now marked **Corrected**, and it is confirmed and checked. Click **Cancel** to close the search without changing anything.

The search looks only in the sets chosen in **Sets (art fallback)**; the line under the search box says which. If the right card isn't listed, clear that set selection at the top of the page and search again.

> [!TIP]
> Corrected cards don't show a confidence percentage or value badges, because those come from the automatic match.

## Card properties

The **Card properties** section of the detail panel sets the details for that copy:

- **Condition** — NM, LP, MP, HP or DMG.
- **Language** — the language of this copy. A small language code (for example *JA*) appears on the row for non-English cards; it is filled in solid when the language was read from the card itself.
- **Quantity** — how many identical copies this scan represents.
- **Purchase price** — what you paid, if you want to track it.
- **Foil** — turn on for a foil copy, then optionally choose or type a **Foil type**.
- **Tags** — pick existing tags or type new ones.
- **Note** — free text, for example *signed, played, misprint…*

### Edit several cards at once

1. Check the cards you want to change.
2. Click **Edit** in the list header. The **Edit N selected cards** window opens.
3. Tick each property you want to apply, then set its value. Unticked properties are left alone.
4. For **Tags**, choose a **Mode**: **Add** keeps each card's existing tags and adds yours; **Replace** swaps them for yours.
5. Click **Apply to N**.

![The Edit selected cards window with Condition and Tags ticked](scanning-bulk-edit.png)

## Filter and sort the list

When the list is long, use the filter bar above it:

- **Filter by name** — shows rows whose card name contains the text.
- **Show** — **All**, **Checked only** or **Unchecked only**.
- **Min confidence %** — hides weaker matches.
- **Min value** — hides cards below a market value.
- **Sort by** — **None** (newest first), **Name**, **Confidence** or **Market value**. The arrow next to it switches between ascending and descending. Cards without a value sort to the bottom.

While a filter or sort is on, you'll see *Showing X of Y* and a **Clear** button.

> [!NOTE]
> Everything in the action bar works only on the cards you can currently see. **Confirm**, **Add**, **Export**, check-all and bulk **Edit** all ignore rows hidden by a filter.

## Value badges

Matched cards can show two kinds of badge:

- A **gold star** means the card is new: you don't own a copy yet.
- One to five **currency signs** (for example $$$) show the card's market value tier. More signs means a more valuable card. Hover over them to see the exact value and the tier's price range.

An administrator sets the currency and the price range for each tier in **Administration ▸ Scan Badges**. See [Administration](help:administration).

## Add cards to a location

1. Click **Add to location…** in the action bar. The **Add scanned cards to location** window opens.
2. Search or scroll the list. Locations are grouped by type and show their card count. Only locations you are allowed to change are listed.
3. Click a location to choose it. Its name now appears on the button.
4. Click **Add N confirmed cards**.

The button stays disabled until a location is chosen, at least one card is confirmed and checked, and every scan has finished matching. When the cards are added you'll see *Added N card(s) to your collection*, and those rows leave the list. Unchecked or unconfirmed scans stay so you can deal with them later.

### Create a location on the spot

If the location doesn't exist yet, you don't have to leave the page:

1. In the location window, click **New location**.
2. Enter a **New location name**. You'll be warned if *This name is already in use*.
3. Choose the **Type** (and the site, if your account can use more than one). For a deck box, also choose its game.
4. Click **Create & select**.

![The Add scanned cards to location window with the New location form open](scanning-location-picker.png)

See [Locations](help:locations) for more about location types and sites.

## Export scans without adding them

You can download your scans as a file without putting anything in your collection, for example to price a stack before you buy it.

1. Check the cards you want to export. Confirming isn't required.
2. Click **Export (N)** and choose a format: **OmniCard (full detail)**, **TCGplayer**, **Moxfield**, **ManaBox**, **Archidekt**, **Deckbox**, **Dragon Shield**, **Card Price Ticker** or **Text list (.txt)**. Moxfield, ManaBox, Archidekt and Deckbox are marked *MTG only* and are available only for Magic: The Gathering.
3. Or choose **Copy as text list** to copy lines such as *4 Lightning Bolt (2X2) 117* to the clipboard, ready to paste into a deck builder.
4. Or choose **Export several formats (.zip)…**, tick the formats you want, and click **Download N formats**. Your choice is remembered for next time in this browser.

Exported scans stay in the list, so you can still add them to a location later.

> [!NOTE]
> If you don't see **Export**, ask an administrator for access.

## Pick up where you left off

Your scan list is saved in this browser as you work. If you refresh the page, close the tab or your browser crashes, the next visit to Scan shows *Restored N scans from your last session*. Scans that were still matching are matched again automatically. Click **Discard** in that message to throw the restored scans away.

The saved list belongs to this browser on this device only. It isn't kept in a private or incognito window, and another person or device won't see it.

## Game-specific notes

- **Magic: The Gathering — The List.** When a scanned card is a reprint from *The List*, OmniCard matches it to that cheaper printing and shows a **The List** chip. If it shows **The List?** instead, the reprint was detected but its printing wasn't found in the catalog, so the printing and price shown may be the more expensive original. Check it before adding.
- **Yu-Gi-Oh! editions.** When the scan shows *1st Edition* or *Limited Edition* text, that edition appears as a chip in the detail panel for reference.
- **Riftbound Battlefields.** Landscape Battlefield cards can be scanned sideways; OmniCard turns them the right way automatically.
- **Language detection.** With **Card language** set to **Auto (detect)**, the printed language is read from Magic: The Gathering and Yu-Gi-Oh! cards.

## Tips and troubleshooting

- **Lots of wrong matches?** Choose the set or sets you are scanning in **Sets (art fallback)** before adding pictures. Make sure the right **Game** is selected.
- **Foils matching badly?** Tick **Foil** before you scan them.
- **Can't read the card in the thumbnail?** Use **View scan** to open the full picture.
- **The add button stays grey.** Check that a location is chosen, that matching has finished for every row, and that at least one card is both confirmed and checked and visible under the current filter.
- **A scan shows Error.** Remove it and try a sharper, well-lit picture.
- Scanner output can also be processed in the background without opening the browser. See [Scan batches](help:scan-batches).
- To check that a location holds exactly what you think it does, scan it in an audit. See [Auditing a location](help:location-audit).
- Added cards appear in your [collection](help:collection) right away.
`,Ze='# Search syntax\n\nOmniCard\'s search boxes understand a Scryfall-style search language: plain words for card names, plus fields like `t:creature`, `set:dom` or `tag:trade` that you can combine with `or`, `-` and parentheses. This topic lists every field and operator for each game.\n\n## Where you can use it\n\nThe same language works in two kinds of search box:\n\n- **Collection search** looks through the cards you own. You\'ll find it on the [Collection](/collection) page, on a location\'s page, and in a binder\'s Unplaced pool. On the Collection and location pages, press **Enter** to run the search. See [Collection](help:collection).\n- **Card search** looks through the full card catalog for a game, including cards you don\'t own. It\'s used when you correct a scan match and when you add a card to a location, binder pocket or list. See [Scanning](help:scanning).\n\nMost fields work in both. Some Magic fields only work in card search, and fields about your own copies (tags, condition, location) only work in collection search. The tables below say which.\n\n> [!TIP]\n> Click the **Search syntax help** icon (**?**) at the right of a collection search box to see the fields for the game selected in the top bar, each with an example you can copy.\n\n![The search syntax help popover listing fields and examples](search-syntax-help-popover.png)\n\n## The basics\n\n| You type | What it finds |\n|---|---|\n| `bolt` | Cards whose name contains "bolt". |\n| `lightning bolt` | Names containing both "lightning" and "bolt". |\n| `"lightning bolt"` | Names containing the exact phrase. |\n| `!"Lightning Bolt"` | Cards named exactly Lightning Bolt. |\n| `t:dragon` | Cards whose type contains "dragon". |\n| `t:dragon c:r` | Both must match (a space means AND). |\n| `t:dragon or t:angel` | Either one can match. |\n| `-is:foil` | Excludes matching cards. |\n| `(t:goblin or t:elf) c:g` | Parentheses group terms. |\n\nThings to know:\n\n- Searches ignore upper and lower case.\n- Put quotes around a value that contains spaces, for example `t:"legendary creature"` or `loc:"red binder"`.\n- `or` can be typed as `or` or `OR`.\n- A field name the game doesn\'t recognize is treated as a name search.\n\n## Operators\n\nPut an operator between the field name and the value, with no spaces: `cmc>=3`.\n\n| Operator | Meaning | Example |\n|---|---|---|\n| `:` | Contains, or "has" for colors and flags | `t:elf` |\n| `=` | Exactly equals | `cond=nm` |\n| `!=` | Doesn\'t equal | `set!=dom` |\n| `<` | Less than | `cmc<3` |\n| `>` | Greater than | `hp>100` |\n| `<=` | Less than or equal | `level<=4` |\n| `>=` | Greater than or equal | `r>=rare` |\n| `-` before a term | Not | `-tag:trade` |\n| `not:` | Same as `-is:` | `not:foil` |\n\nThe comparison operators (`<`, `>`, `<=`, `>=`) only work on fields that hold numbers or ordered values. The tables below mark these fields with "supports < >".\n\n## Fields for every game\n\nThese fields work in collection search for every game.\n\n| Field | Short forms | Example | What it matches |\n|---|---|---|---|\n| name | `n` | `name:bolt` | Card name (bare words do the same). |\n| set | `s`, `e`, `edition` | `set:dom` | Set code, exactly. |\n| cn | `number` | `cn:123` | Collector number, exactly. |\n| type | `t` | `t:creature` | Words in the card\'s type. |\n| rarity | `r` | `r:rare` | Rarity. Supports < > for Magic rarities. |\n| color | `c`, `id`, `ci`, `identity`, `commander` | `c:wu` | Magic colors (see below). |\n| condition | `cond` | `cond:nm` | Your copy\'s condition. |\n| lang | `language` | `lang:ja` | Your copy\'s language. |\n| location | `loc` | `loc:binder` | The name of the location the card is in. |\n| tag | `tags` | `tag:trade` | One of your tags on the card. |\n| is | `not` | `is:foil` | Flags on your copy (see below). |\n| foil | | `foil:true` | Foil (`true`) or non-foil (`false`). |\n\n### Details\n\n- **Rarity order.** For Magic, `r>=rare` finds rares and mythics, and `r<rare` finds commons and uncommons. The order is common, uncommon, rare, mythic. For other games, use the rarity name, for example `r:"super rare"`.\n- **Language.** Use a code such as `en`, `ja`, `de`, `fr`, `it`, `es`, `pt`, `ko`, `ru`, `zhs` or `zht`. Many spellings also work, such as `lang:jp` or `lang:japanese`. Cards with no language set count as English.\n- **Tags.** `tag:foo` matches any tag containing "foo". `tag=foo` matches the tag "foo" exactly. `-tag:foo` finds cards without it.\n- **Location.** `loc:binder` matches every location with "binder" in its name. Use `loc="Red Binder"` for one exact location.\n\n### is: flags\n\n| Flag | Finds |\n|---|---|\n| `is:foil` | Foil copies. |\n| `is:missing` | Copies flagged as missing, for example by an audit. |\n| `is:missingdb` | Copies flagged because the card couldn\'t be found in the card catalog. |\n\nUse `-is:foil` or `not:foil` for the opposite.\n\n### Magic colors\n\nColors use the letters W (white), U (blue), B (black), R (red) and G (green), or the words `white`, `blue`, `black`, `red`, `green`.\n\n| You type | Finds |\n|---|---|\n| `c:r` | Cards that include red. |\n| `c:wu` or `c>=wu` | Cards that include white and blue (and maybe more). |\n| `c=wu` | Exactly white and blue. |\n| `c<=wu` | Only white, blue, or both. Nothing else. |\n| `c!=wu` | Anything except exactly white and blue. |\n| `c:colorless` or `c:c` | Colorless cards and lands. |\n| `c:multicolor` or `c:multi` | Cards with two or more colors. |\n\nIn collection search, `id:` behaves the same as `c:`. Card search treats them separately (see below).\n\n> [!NOTE]\n> The **price** and **date** fields appear in the help list but don\'t filter your collection yet. To find your most valuable cards, sort the list by the **Market** column instead.\n\n## Magic: The Gathering\n\n### Extra fields in collection search\n\nWhen Magic is selected in the top bar, collection search also understands these fields.\n\n| Field | Short forms | Example | What it matches |\n|---|---|---|---|\n| oracle | `o` | `o:"draw a card"` | Words in the rules text. |\n| fulloracle | `fo` | `fo:trample` | Rules text including reminder text. |\n| flavor | `ft` | `ft:goblin` | Words in the flavor text. |\n| artist | `a` | `a:"rebecca guay"` | Illustrator name. |\n| watermark | `wm` | `wm:azorius` | Watermark. |\n| cmc | `mv`, `manavalue` | `cmc>=7` | Mana value. Supports < >. |\n\nFor `cmc`, use `-cmc:3` rather than `cmc!=3`.\n\nMagic has more fields than these, such as `pow`, `kw` or `f:modern`. They\'re listed in the **?** help, but in collection search they\'re treated as a name search. Use them in card search instead.\n\n### Card search (full Scryfall syntax)\n\nWhen you look up a Magic card to add or to correct a scan, the search supports nearly all of [Scryfall\'s syntax](https://scryfall.com/docs/syntax).\n\n| Field | Short forms | Example | What it matches |\n|---|---|---|---|\n| name | `n` | `n:bolt` | Card name. |\n| set | `s`, `e`, `edition` | `s:dom` | Set code, or words in the set name. `set=` matches the code only. |\n| block | | `block:innistrad` | Words in the set name. |\n| st | `settype` | `st:masters` | Set type (expansion, masters, commander, …). |\n| cn | `number` | `cn>=300` | Collector number. Supports < >. |\n| type | `t` | `t:"legendary creature"` | Type line. |\n| oracle | `o` | `o:"~ deals 3"` | Rules text. `~` stands for the card\'s own name. |\n| fulloracle | `fo` | `fo:trample` | Rules text including reminder text. |\n| keyword | `kw` | `kw:flying` | Keyword ability. |\n| mana | `m`, `manacost` | `m:{2}{W}{W}` | Mana cost. `m:2ww` also works. `=` means exactly. |\n| cmc | `mv`, `manavalue` | `mv<=2` | Mana value. Supports < >. |\n| power | `pow` | `pow>=5` | Power. Supports < >, and `pow>tou`. |\n| toughness | `tou` | `tou<3` | Toughness. Supports < >. |\n| loyalty | `loy` | `loy>=5` | Starting loyalty. Supports < >. |\n| defense | `def` | `def>=4` | Battle defense. Supports < >. |\n| pt | `powtou` | `pt:2/2` | Power and toughness together. |\n| colors | `c`, `color` | `c:rg` | Card colors. |\n| identity | `id`, `ci`, `commander` | `id<=wu` | Color identity, for Commander. |\n| produces | | `produces:g` | Colors of mana the card can make. |\n| devotion | | `devotion>=3` | Number of colored mana symbols. Supports < >. |\n| rarity | `r` | `r>=rare` | Rarity. Supports < >. |\n| artist | `a` | `a:"rebecca guay"` | Illustrator. |\n| flavor | `ft` | `ft:goblin` | Flavor text. |\n| watermark | `wm` | `wm:azorius` | Watermark. |\n| has | | `has:watermark` | `watermark`, `indicator` or `flavor`. |\n| border | | `border:borderless` | Black, white, silver or borderless. |\n| frame | | `frame:showcase` | Frame year (`2015`) or effect (`showcase`, `extendedart`). |\n| stamp | | `stamp:acorn` | Security stamp. |\n| layout | | `layout:transform` | Card layout. |\n| game | | `game:arena` | Where it\'s available: paper, mtgo or arena. |\n| in | | `in:paper` | Same as `game:`. |\n| lang | `language` | `lang:ja` | Printing language. |\n| year | | `year>=2020` | Release year. Supports < >. |\n| date | | `date>=2024-01-01` | Release date. Supports < >. |\n| usd | | `usd<1` | US dollar price. Supports < >. |\n| eur | | `eur<1` | Euro price. Supports < >. |\n| tix | | `tix<5` | MTGO ticket price. Supports < >. |\n| edhrec | | `edhrec<1000` | EDHREC popularity rank (lower is more popular). |\n| format | `f`, `legal` | `f:modern` | Legal or restricted in a format. |\n| banned | | `banned:legacy` | Banned in a format. |\n| restricted | | `restricted:vintage` | Restricted in a format. |\n\nColors in card search work like the **Magic colors** table above, and also accept `m` for multicolor and a number for how many colors a card has, for example `c>=2`.\n\n#### is: flags in card search\n\nIn card search, `is:` describes the printing, not your copy.\n\n| Group | Flags |\n|---|---|\n| Finish | `is:foil`, `is:nonfoil`, `is:etched`, `is:glossy` |\n| Printing | `is:promo`, `is:reprint`, `is:firstprint`, `is:reserved`, `is:digital`, `is:booster`, `is:oversized`, `is:variation`, `is:hires` |\n| Art | `is:fullart`, `is:textless`, `is:spotlight` |\n| Mana | `is:colorless`, `is:multicolor` (or `is:gold`), `is:hybrid`, `is:phyrexian` |\n| Layout | `is:split`, `is:flip`, `is:transform`, `is:meld`, `is:leveler`, `is:dfc`, `is:mdfc`, `is:adventure`, `is:token` |\n| Card kind | `is:permanent`, `is:spell`, `is:land`, `is:creature`, `is:vanilla`, `is:commander` |\n| Other | `is:gamechanger`, `is:contentwarning`, `is:funny` |\n\n#### Ordering card search results\n\nAdd these anywhere in a card search to change the result order. They don\'t filter anything.\n\n| Directive | Values |\n|---|---|\n| `order:` | `name`, `cmc`, `power`, `toughness`, `loyalty`, `released`, `rarity`, `color`, `usd`, `eur`, `tix`, `edhrec`, `set`, `artist`, `cn` |\n| `direction:` | `asc` or `desc` |\n| `unique:` | `cards` (one result per card name), `art` (one per artwork) or `prints` (every printing, the default) |\n\nExample: `t:dragon order:usd direction:desc` lists the priciest dragons first.\n\n> [!NOTE]\n> `order:` only works in card search. To sort your collection, click a column header on the Collection page.\n\n## One Piece Card Game\n\n| Field | Short forms | Example | What it matches |\n|---|---|---|---|\n| cost | | `cost:4` | Play cost. |\n| power | `pow` | `power:5000` | Power. |\n| counter | `ctr` | `counter>=1000` | Counter value. Supports < >. |\n| life | | `life:5` | Leader life. |\n| attribute | `attr` | `attribute:slash` | Attribute (Slash, Strike, …). |\n| subtype | `sub`, `trait` | `subtype:straw` | Subtype or trait. |\n\n`cost` and `power` match the value you type. Only `counter` supports < >. In card search, `color:red` also finds cards by color, and `set:` matches the set code or set name.\n\n## Riftbound\n\n| Field | Short forms | Example | What it matches |\n|---|---|---|---|\n| domain | `d` | `domain:body` | Any of a card\'s domains. |\n| energy | | `energy>=4` | Energy cost. Supports < >. |\n| might | `m` | `might>=5` | Might. Supports < >. |\n| power | `pow` | `power>=3` | Power. Supports < >. |\n| supertype | `super` | `supertype:champion` | Supertype. |\n\nIn card search, `set:` matches the set code or set name, and `cn:` supports < >.\n\n## Pokémon\n\n| Field | Short forms | Example | What it matches |\n|---|---|---|---|\n| hp | | `hp>=200` | Hit points. Supports < >. |\n| stage | | `stage:basic` | Evolution stage. |\n\n## Yu-Gi-Oh!\n\n| Field | Short forms | Example | What it matches |\n|---|---|---|---|\n| attribute | `attr` | `attribute:dark` | Monster attribute (DARK, LIGHT, …). |\n| level | `lvl`, `rank` | `level>=8` | Level or Rank. Supports < >. |\n| atk | | `atk>=3000` | ATK. Supports < >. |\n| def | | `def>=2500` | DEF. Supports < >. |\n\n## Final Fantasy TCG\n\n| Field | Short forms | Example | What it matches |\n|---|---|---|---|\n| element | `e`, `el` | `element:fire` | Element. |\n| cost | | `cost>=5` | Casting cost. Supports < >. |\n| power | `pow` | `power>=8000` | Power. Supports < >. |\n| job | | `job:warrior` | Job. |\n| category | `cat` | `category:vii` | Category, for example VII or XIV. |\n\nElement shorthands: `f` Fire, `i` Ice, `l` Lightning, `w` Water, `wi` Wind, `ea` Earth, `li` Light, `d` Dark. So `e:f` finds Fire cards.\n\n> [!WARNING]\n> In Final Fantasy TCG, `e:` means element, not set. Use `s:` or `set:` to search by set.\n\nFor Pokémon, Yu-Gi-Oh! and Final Fantasy TCG, `<` and `>` compare numbers when both sides are numbers. In card search, `set:` matches the set code or set name.\n\n## Searching with All Games selected\n\nWhen **All Games** is selected in the top bar, collection search still works across every game, with two differences:\n\n- Game fields only work by their full name, such as `artist:`, `cmc>=3`, `element:fire` or `hp>=100`. Short forms that belong to one game (like `a:` or `mv`) are treated as a name search.\n- A field several games share, such as `power`, matches cards from each game that has it.\n\nSelect a single game for the full set of short forms.\n\n## Example searches\n\n| Search | Finds |\n|---|---|\n| `t:creature c=g cmc<=2` | Mono-green creatures with mana value 2 or less (Magic). |\n| `r>=rare -is:foil loc:bulk` | Non-foil rares and mythics in locations named "bulk". |\n| `tag:trade or tag:sell` | Cards tagged trade or sell. |\n| `lang:ja is:foil` | Japanese foil copies. |\n| `set:dom -cond:nm` | Dominaria cards that aren\'t Near Mint. |\n| `o:"draw a card" t:instant` | Instants that draw a card (Magic). |\n| `e:f cost>=5` | Fire cards costing 5 or more (Final Fantasy TCG). |\n| `hp>=200 stage:basic` | Basic Pokémon with 200+ HP. |\n| `level>=8 attr:dark` | Level 8+ DARK monsters (Yu-Gi-Oh!). |\n| `might>=5 domain:body` | Body cards with 5+ might (Riftbound). |\n| `t:dragon order:usd direction:desc` | Dragons, most expensive first (Magic card search). |\n\n## Troubleshooting\n\n- **No results.** Check that the right game is selected in the top bar, and that values with spaces are in quotes.\n- **A field seems to be ignored.** It may not apply where you\'re searching. For example, `kw:flying` only works in card search, and `tag:` only works in collection search.\n- **`set:` finds nothing.** In collection search, `set:` needs the set code, such as `set:dom`, not the set name.\n- **Numbers compare oddly.** Only fields marked "supports < >" compare by value. Others match the text you type.\n',en=`# Sets

The Sets page shows every card printed in a set and which ones you own, so you can track how close you are to completing it.

## Open a set checklist

1. Choose a game in the top bar. The Sets page needs a single game, not **All Games**.
2. Open [Sets](/sets) from the navigation menu.
3. Click the **Set** box and pick a set. Type part of the set name or code to narrow the list.

The checklist for that set loads below.

![The Sets page with a set selected, the completion bar, and the checklist](sets-checklist.png)

> [!NOTE]
> If you see *Pick a game in the top bar to browse its sets*, the top bar is set to **All Games**. Choose a game there first.

## Read your progress

Above the checklist, a summary line shows the set name, how many different cards you own out of the total, and the percentage, for example *Dominaria — 182/269 owned (67.7%)*. The bar underneath fills as you complete the set.

A card counts as owned if you have at least one copy of it, in any condition, foil or non-foil.

## The checklist

Every card in the set is listed in collector-number order.

| Column | What it shows |
|---|---|
| No. | Collector number. |
| Name | Card name. Hover over it to see the card art. |
| Rarity | Printed rarity. |
| Owned | A green **×N** badge with how many copies you own, or a dash if you have none. |
| Normal | Current market price for a regular copy. |
| Foil | Current market price for a foil copy, if one exists. |

Cards you don't own are shown faded, so the gaps in your set stand out.

Click a column header to sort the checklist, for example by **Normal** price to see which missing cards cost the most. Large sets are split into pages. Use the controls at the bottom of the list to move between them.

## What counts toward your total

- Copies in all your locations count, wherever they are stored.
- Cards you've traded away don't count.
- Checklists list the English printings of each set.

To see where your copies of a card are, search for it on the [Collection](/collection) page, for example \`set:dom cn:123\`. See [Collection](help:collection) and [Search syntax](help:search-syntax).

## Troubleshooting

- **The set list is empty.** The card catalog for that game hasn't been downloaded yet. An administrator can download it from **Administration ▸ Catalog Data**. See [Administration](help:administration).
- **Prices are blank.** No price is available for that card yet. Prices update when the catalog is refreshed.
- **A card I own shows as not owned.** Check that the card in your collection has the right set and collector number. Open it from the [Collection](/collection) page and compare it with the checklist.
`,nn=`# Trades

Record the cards you trade away, including cards picked up at a show that were never in OmniCard. Note what you got in return, and keep a history with values and photos.

## What trades are for

When you trade cards with someone, the Trades page keeps your collection accurate. You build a trade from the cards you're giving away, then finalize it with a note about what you received. You can add the value you received and a photo too. OmniCard marks the cards as traded away and adds the trade to your history. The history shows whether each trade gained or lost value.

Open it from **Trades** in the navigation menu, or go to [Trades](/trades).

![The Trades page with a trade in progress above the trade history](trades-builder.png)

> [!NOTE]
> If you don't see **Trades** in the menu, or you can't start, finalize, or cancel a trade, ask an administrator for access.

## Make a trade

### 1. Start the trade

On [Trades](/trades), click **New trade** under **Start a trade**. The panel changes to **Trade in progress**.

You can also start from any card. Open the card's details, for example by clicking it in [Collection](/collection), and click **Add to trade**. The card is added to your trade in progress, or a new trade starts. A message reminds you to finalize it on the Trades page.

### 2. Add cards you own

1. Type in **Add a card you own**. You can search by name, and narrow it with \`set:\` and \`cn:\`. For example, \`bolt set:2x2 cn:117\`.
2. Matching cards appear below the box, with set, number, condition, and market price.
3. Click a card to add it to the trade.

Only cards at sites you're allowed to change appear in the search. A card that's already in the trade can't be added twice.

### 3. Add cards that aren't in your collection

At a card show, you might trade away something you picked up that day and never entered into OmniCard. You can still record it:

1. Click **Add off-catalog card (card-show pickup)**.
2. Fill in **Card name (optional)** and **Estimated value (optional)**.
3. Click **Photo (optional)** to attach a picture. On a phone, this opens the camera.
4. Click **Add card**.

Off-catalog cards show *(off-catalog)* after their name. Click **Hide off-catalog card** to close the form.

### 4. Check the cards

Each card in the trade shows its picture, set, number, and value. Cards from your collection also have a **TCGplayer** link to check the current price. To take a card out, click its remove icon.

The header shows a running total, such as **Giving 3 cards · $42.50**. For cards from your collection, the value is the card's market price. For off-catalog cards, it's the estimated value you entered.

### 5. Finalize the trade

1. Under **Finalize**, describe what you got in **Note — what did you get?**
2. Optionally, enter **Value received (optional)**. OmniCard uses this to show whether the trade gained or lost value.
3. Optionally, click **Photo of received cards (optional)** to attach a picture of what you got.
4. Click **Finalize trade** and confirm.

When you finalize, the trade is applied to your collection right away:

- The cards you gave away are marked as traded. They no longer count toward your collection's value, and lists and new trades can't use them.
- The trade appears at the top of your **History**.

> [!NOTE]
> Finalizing doesn't add the cards you received to your collection. Scan or import them as usual. See [Scanning](help:scanning) and [Importing](help:importing).

## Cancel a trade

To drop a trade in progress, click **Cancel trade** and confirm. Nothing has been applied yet, so your collection doesn't change.

## Come back to a trade later

A trade in progress is saved as you go. You can leave the Trades page and come back later, in the same browser, and pick up where you left off. If you use **Add to trade** on a card, the card goes into the same trade in progress.

## Trade history

Under **History**, the cards you've traded away are listed newest first. Each trade shows:

- The first card's name, plus how many more cards were in the trade.
- A camera icon if you attached a photo of the received cards.
- The value change, if you entered a value received. It's green when you received at least as much as you gave, and orange when you received less.
- The date of the trade.

Click a trade to expand it and see:

- **Out**: the total value of the cards you gave away.
- **Received**: the value you entered, or a dash if you didn't enter one.
- Your note about what you got.
- **Traded away**: every card in the trade, with its set, number, finish, value, and *(off-catalog)* when it wasn't from your collection.

![An expanded trade in the history showing values, the note, and the traded-away cards](trades-history.png)

## Tips

- Enter a **Value received** for every trade. Over time, the value changes show whether your trades are paying off.
- At a show, add photos from your phone as you trade. They're a handy record if a deal is questioned later.
- To find a card fast, search by set code and collector number, such as \`set:dom cn:123\`.
- For cards you sold instead of traded, use [Sales](help:sales).
`,tn=`# Troubleshooting

Answers to the problems people run into most often, from missing sections and wrong matches to missing prices, edit conflicts and camera trouble. Each answer starts with the message you might see, where there is one.

## A section or button is missing

*You don't have permission to view this section. Ask an administrator if you need access.*

*You don't have permission to do that.*

What you can see and do in OmniCard depends on your role and permissions. If a sidebar section, a tab on the Administration page, or a button such as **Delete** is missing or refuses to work, your account doesn't include that permission.

- Ask an administrator to change your role or give you the permission. See [Administration](help:administration).
- Once they've saved the change, move to another page. Your sidebar and buttons update without signing out.
- Some things are for administrators only, whatever their role: managing users, roles and sites, watched scan folders, and changing receipt and scan badge settings.

![The message shown when you open a section you don't have access to](troubleshooting-no-access.png)

## You can see a site but can't change it

*You only have read access to that site.*

*This card is in …, which you can only view. Ask an administrator for write access to change it.*

An administrator can give you **Read** or **Write** access to each site. With **Read** you can browse and search its locations and cards, and add them to lists, but you can't change them. On the Locations page these locations are marked **view only**.

When moving a list's cards into place, you might also see *Some cards on this list would be moved out of a site you only have read access to.* Move those cards yourself somewhere you can write to, or ask for **Write** access.

Ask an administrator for **Write** access to the site. See [Administration](help:administration).

## Cards or locations seem to be missing

Check these before assuming something is lost:

- **The game selector.** The selector in the top bar filters most pages to one game. Choose **All Games** to see everything.
- **The site filter.** The Locations page has a **Site** filter. Choose **All Sites**.
- **Hide empty locations.** On the Locations page, empty locations are hidden while this switch is on.
- **The search box.** Clear any search text on the Collection page.
- **Site access.** If cards are in a site you don't have access to, you won't see them at all. Ask an administrator.
- **Trades.** Cards you've traded away no longer count as part of your collection once the trade is finalized. See [Trades](help:trades).

## The Sets page says to pick a game

*Pick a game in the top bar to browse its sets.*

The Sets page shows one game at a time. Choose a game in the top bar instead of **All Games**. See [Sets](help:sets).

## A card matched the wrong printing

OmniCard recognizes a card by its artwork and, for most games, by reading the set code and collector number printed on it. Sometimes it picks the wrong card or the wrong printing of the right card. Fix it on the Scan page before you add the card:

1. Select the card in the scan list.
2. Click **Search catalog**.
3. Type the card's name and pick the correct card and printing from the results.
4. The card is now marked **Corrected** and is already confirmed, so you can add it as usual.

![Correcting a scan with Search catalog](troubleshooting-search-catalog.webp)

OmniCard remembers your corrections when you add the cards, so it gets better at matching that card in future scans.

> [!TIP]
> If the right printing doesn't show up in the search, check **Sets (art fallback)** at the top of the Scan page. When sets are chosen there, the search only looks inside those sets. Remove them to search every set.

If you've already added the wrong card, open it in the [Collection](help:collection), delete it, and add the correct card. You can rescan it, or use **Add card** on its location. See [Scanning cards](help:scanning).

## A scan says No match

*No confident match. Use "Search catalog" to pick the correct card, or "View scan" to read it.*

OmniCard couldn't recognize the card well enough to guess. Click **View scan** to see the photo, then **Search catalog** to pick the card yourself. Common causes:

- **The wrong game is selected.** Check the **Game** at the top of the Scan page.
- **The set is too new.** Ask an administrator to run **Download catalog** for that game.
- **The photo is unclear.** See [Take better photos with your phone](help:troubleshooting#take-better-photos-with-your-phone).
- **Sets (art fallback) is set to the wrong sets.** Clear it to match against all sets.
- **The card is in another language** and the catalog only has English. An administrator can add languages under **Administration ▸ Catalog Data ▸ Languages to download**, for the games that offer them.

## Prices are missing or look wrong

Market prices come from each game's price source, and an administrator refreshes them on the server.

- **Prices are out of date.** Ask an administrator to run **Update prices** for the game under **Administration ▸ Catalog Data**.
- **One card has no price.** Some printings, such as certain promos and older editions, simply don't have a price at the source. OmniCard can't fill those in. A card with no price shows *n/a* when you open it and counts as zero in totals.
- **A foreign card shows the English price.** Non-English printings rarely have their own price, so they use the English printing's price. Japanese Pokémon cards have their own prices.
- **Foil and non-foil prices differ.** Check that the card's **Foil** setting is right. Foil copies use the foil price.
- **Sealed product** has its own market price on the Inventory page. See [Inventory](help:inventory).

See [Dashboard](help:dashboard) for how prices feed into your totals.

## Card images are missing

- **New cards with no picture.** The catalog may not have the newest set yet. Ask an administrator to run **Download catalog** for that game.
- **Images load slowly or not at all.** By default, card images can come from outside websites. An administrator can run **Download artwork** under **Administration ▸ Catalog Data** to keep a copy of every image on your server.
- **A few cards never get a picture.** Some cards have no image at the source.

## Someone else changed this item

*This item was changed by someone else. Reload and try again.*

Another person, or another tab or device of yours, saved a change to the same card, order or record after you opened it. OmniCard stopped your save so it wouldn't overwrite their change.

1. Reload the page.
2. Check the item's current details.
3. Make your change again and save.

## Sign-in problems

**Incorrect username or password.** Check your typing. Passwords are case-sensitive, so check Caps Lock too. If it still fails, ask an administrator to reset your password under **Administration ▸ Users**.

**You keep getting signed out.** If you don't tick **Remember me** when you sign in, you're signed out when you close the browser. Ticking it keeps you signed in on that device for up to 30 days. Clearing your browser's cookies also signs you out.

**Not authenticated. Please sign in.** Your session ended while the page was open. Reload the page and sign in again.

**You forgot your password.** Ask an administrator to reset it. After signing in with the new password, you can change it yourself under **Administration ▸ Users**.

**You're setting up a new server.** Sign in with the built-in **Admin** account (password \`admin\`), then change that password straight away.

## Take better photos with your phone

On a phone, **Take photo** on the Scan page opens the rear camera. For the best matches:

- Photograph **one card per photo**.
- Fill most of the frame with the card, keeping the whole card in view.
- Hold the phone **straight above the card** so the card isn't skewed.
- Use **even light**. Avoid glare and reflections, especially on foils and cards in sleeves or top-loaders. Tilting the card slightly away from a lamp often helps.
- Put the card on a **plain, dark background** that contrasts with its border.
- Make sure the bottom of the card, where the set code and collector number are, is **in focus**. That's what OmniCard reads to find the exact printing.
- Before scanning a pile, set **Condition**, **Card language** and **Foil** at the top of the Scan page. Each new photo gets those settings.

See [Scanning cards](help:scanning).

## The webcam won't start

*Camera permission was denied. Allow camera access in your browser and try again.* Click the camera or lock icon in the browser's address bar, allow the camera, then close and reopen **Use webcam**.

*No camera was found. Connect a webcam and try again.* Check the webcam is plugged in and not in use by another app, such as a video call.

*Could not start the camera.* Browsers only allow a live camera on secure (https) connections, or when OmniCard runs on the same computer. If you open OmniCard by a plain local network address, the live webcam may be blocked. Use **Take photo** or **Add images** instead, or ask whoever runs the server about a secure address.

*Failed to load the image-processing engine.* Reload the page and try again. Your browser needs to be able to download it the first time.

## An image was rejected

*Only JPEG, PNG and TIFF images are accepted.* Save or export the photo as JPEG or PNG and try again. Some phones save photos in other formats by default.

*Image exceeds 30 MB limit.* Use a smaller image, or a lower resolution on your scanner. Card photos don't need to be huge.

## Your scan list disappeared

*Restored … scans from your last session.*

Scans you haven't added yet are kept in your current browser on this device, so a refresh or crash doesn't lose them. They aren't shared with other browsers or devices, and a private or incognito window can't keep them. To keep scans safe, add them to a location, or use **Export** to download them without adding them.

Scans from a watched scanner folder work differently. They're stored on the server and listed under **Scan batches** on the Scan page. See [Scan batches](help:scan-batches).

## Scan batch problems

*You're no longer reviewing this batch (someone else took it over, or it was closed). Your last changes may not have been saved.* Only one person can review a batch at a time, and someone else clicked **Take over**, or the batch was closed. Reopen it from the Scan page and check your work.

*Nobody is reviewing this batch. Start reviewing it to make changes.* Click **Review this batch** to claim it.

**A batch never appears.** Check that the scanner saves into a subfolder of the game's watched folder, and wait for the quiet period to pass. An administrator can check the folder's status under **Administration ▸ Scan Badges**. See [Administration](help:administration) and [Scan batches](help:scan-batches).

## A card can't be moved, split or edited

*Already listed for sale. Unlist it from Sales ▸ Listings to change.*

A card that's listed for sale is locked against changes that would affect the listing, such as splitting its stack. Remove the listing on **Sales ▸ Listings** first. See [Sales](help:sales).

*This deck box only holds … cards.* A deck box is tied to one game, so you can't put another game's cards in it. See [Deck boxes](help:deck-boxes).

## Imports fail

*Couldn't fetch that deck URL. Supported: Moxfield, Archidekt (public decks).* Check the link is a public Moxfield or Archidekt deck. Private decks can't be read.

**Some cards couldn't be found.** OmniCard lists them after the import. Check their spelling, or that the catalog has their set. When the exact printing isn't in the catalog, OmniCard uses another printing of the same card and tells you which.

**Importing into a location changed nothing.** Imports started from a location page are all or nothing. If any line has a problem, nothing is imported and every problem is listed so you can fix the file and try again.

See [Importing](help:importing).

## Catalog jobs won't start

*A catalog refresh is already running.* Only one catalog job runs at a time. Wait for it to finish. Its progress shows on the **Catalog Data** tab.

**A job finished almost instantly.** That's usually fine. If little has changed since the last run, there's little to do. Look for the ✓ under **Recent**. A ✗ means it failed, and the message next to it says why.

See [Administration](help:administration).

## eBay errors

*Connect to eBay first.* OmniCard isn't linked to your eBay account. An administrator, or someone with eBay permissions, can connect it under **Administration ▸ eBay**.

*Listed locally, but the eBay push failed.* The card is listed in OmniCard but eBay rejected the listing. Read the error, fix the problem (often the seller settings), and try again. See [eBay](help:ebay).

## Still stuck

- Reload the page. Many temporary problems clear up after a refresh.
- Check you're on the right game and site.
- Ask your OmniCard administrator. If you are the administrator, check the **Recent** results on the **Catalog Data** tab, and the folder status under **Scan Badges**.
`,an="/app/assets/administration-catalog-data-BjeNaI2j.png",on="/app/assets/administration-scan-folders-CS75rQqx.png",sn="/app/assets/administration-site-access-D6UivBxH.png",rn="/app/assets/administration-tabs-BpCoGDvV.png",cn="/app/assets/administration-tabs-BpCoGDvV.png",dn="/app/assets/binders-add-to-pocket-CfrpNCbi.png",ln="/app/assets/binders-edit-mode-2WRu345_.webp",hn="/app/assets/binders-spread-view-C21YWD10.webp",pn="/app/assets/collection-card-details-CEnyiCh8.png",un="/app/assets/collection-decklist-check-DfYRH2pK.png",mn="/app/assets/collection-overview-DXPiQzZD.png",gn="/app/assets/collection-select-actions-DeNskt8b.png",yn="/app/assets/dashboard-overview-BgdtwCHP.png",wn="/app/assets/deck-boxes-panel-DX0XHQ-4.png",fn="/app/assets/deck-boxes-stacked-view-D-RtwJGJ.webp",bn="/app/assets/collection-overview-DXPiQzZD.png",kn="/app/assets/getting-started-phone-menu-AejRhoOH.png",vn="/app/assets/getting-started-sign-in-DnUx9vwZ.png",Cn="/app/assets/help-home-DYGYTGEX.png",xn="/app/assets/importing-page-CLlMp9uw.png",Tn="/app/assets/inventory-new-product-wyjY3g5p.png",Sn="/app/assets/inventory-page-D7A7yRt0.png",An="/app/assets/inventory-product-drawer-Ceku7CC-.png",In="/app/assets/lists-add-card-dialog-MhQSjlN6.png",_n="/app/assets/lists-find-in-collection-BXe0H6i7.png",On="/app/assets/lists-overview-B2IW5FTm.png",Bn="/app/assets/lists-put-cards-away-DKGvHszG.png",Ln="/app/assets/location-audit-button-_OdyGPQm.png",Pn="/app/assets/location-audit-page-DsxC_qed.webp",Mn="/app/assets/location-audit-summary-CKlkpN6V.png",En="/app/assets/locations-add-bar-BLkpDoqv.png",Dn="/app/assets/locations-detail-page-C66E_qD-.png",Nn="/app/assets/locations-import-dialog-C_NxOs9C.png",Fn="/app/assets/locations-page-overview-BnNQ8JST.png",Un="/app/assets/locations-row-menu-o6_KiRtY.png",Rn="/app/assets/sales-customers-DVrg8-v6.png",Wn="/app/assets/sales-import-orders-map-BS_aWYm3.png",jn="/app/assets/sales-list-for-sale-BfBz83FT.png",Gn="/app/assets/sales-listings-Q6MD86Iw.png",Yn="/app/assets/sales-order-detail-Dqrg7sx7.png",Hn="/app/assets/sales-orders-board-z3m97wnu.png",zn="/app/assets/sales-receipt-BKyV_jeV.png",Vn="/app/assets/sales-settings-for-sale-location-BvzarQMv.png",qn="/app/assets/scan-batches-panel-BH8rVmPX.png",$n="/app/assets/scan-batches-review-BatFmhN3.webp",Qn="/app/assets/scanning-bulk-edit-D_dyVKxA.png",Jn="/app/assets/scanning-detail-panel-CkbIVz1U.webp",Kn="/app/assets/scanning-location-picker-Ck5QBotk.png",Xn="/app/assets/scanning-overview-BrL5uZH0.webp",Zn="/app/assets/search-syntax-help-popover-B7d99PGs.png",et="/app/assets/sets-checklist-Cq7lxxc_.png",nt="/app/assets/trades-builder-BzkRc2Uo.png",tt="/app/assets/trades-history-O80-mo8S.png",at="/app/assets/troubleshooting-no-access-Egsybxdj.png",ot="/app/assets/troubleshooting-search-catalog-DJ707UXg.webp",F=Object.assign({"./en-US/administration.md":Ue,"./en-US/binders.md":Re,"./en-US/collection.md":We,"./en-US/dashboard.md":je,"./en-US/deck-boxes.md":Ge,"./en-US/ebay.md":Ye,"./en-US/getting-started.md":He,"./en-US/importing.md":ze,"./en-US/inventory.md":Ve,"./en-US/lists.md":qe,"./en-US/location-audit.md":$e,"./en-US/locations.md":Qe,"./en-US/sales.md":Je,"./en-US/scan-batches.md":Ke,"./en-US/scanning.md":Xe,"./en-US/search-syntax.md":Ze,"./en-US/sets.md":en,"./en-US/trades.md":nn,"./en-US/troubleshooting.md":tn}),st=Object.assign({"./images/administration-catalog-data.png":an,"./images/administration-scan-folders.png":on,"./images/administration-site-access.png":sn,"./images/administration-tabs.png":rn,"./images/administration-users.png":cn,"./images/binders-add-to-pocket.png":dn,"./images/binders-edit-mode.webp":ln,"./images/binders-spread-view.webp":hn,"./images/collection-card-details.png":pn,"./images/collection-decklist-check.png":un,"./images/collection-overview.png":mn,"./images/collection-select-actions.png":gn,"./images/dashboard-overview.png":yn,"./images/deck-boxes-panel.png":wn,"./images/deck-boxes-stacked-view.webp":fn,"./images/getting-started-layout.png":bn,"./images/getting-started-phone-menu.png":kn,"./images/getting-started-sign-in.png":vn,"./images/help-home.png":Cn,"./images/importing-page.png":xn,"./images/inventory-new-product.png":Tn,"./images/inventory-page.png":Sn,"./images/inventory-product-drawer.png":An,"./images/lists-add-card-dialog.png":In,"./images/lists-find-in-collection.png":_n,"./images/lists-overview.png":On,"./images/lists-put-cards-away.png":Bn,"./images/location-audit-button.png":Ln,"./images/location-audit-page.webp":Pn,"./images/location-audit-summary.png":Mn,"./images/locations-add-bar.png":En,"./images/locations-detail-page.png":Dn,"./images/locations-import-dialog.png":Nn,"./images/locations-page-overview.png":Fn,"./images/locations-row-menu.png":Un,"./images/sales-customers.png":Rn,"./images/sales-import-orders-map.png":Wn,"./images/sales-list-for-sale.png":jn,"./images/sales-listings.png":Gn,"./images/sales-order-detail.png":Yn,"./images/sales-orders-board.png":Hn,"./images/sales-receipt.png":zn,"./images/sales-settings-for-sale-location.png":Vn,"./images/scan-batches-panel.png":qn,"./images/scan-batches-review.webp":$n,"./images/scanning-bulk-edit.png":Qn,"./images/scanning-detail-panel.webp":Jn,"./images/scanning-location-picker.png":Kn,"./images/scanning-overview.webp":Xn,"./images/search-syntax-help-popover.png":Zn,"./images/sets-checklist.png":et,"./images/trades-builder.png":nt,"./images/trades-history.png":tt,"./images/troubleshooting-no-access.png":at,"./images/troubleshooting-search-catalog.webp":ot}),G=[{key:"start",topics:["getting-started","dashboard"]},{key:"scanning",topics:["scanning","scan-batches","location-audit"]},{key:"collection",topics:["collection","search-syntax","sets"]},{key:"organizing",topics:["locations","binders","deck-boxes"]},{key:"lists",topics:["lists","importing","trades"]},{key:"selling",topics:["inventory","sales","ebay"]},{key:"admin",topics:["administration","troubleshooting"]}],it=G.flatMap(e=>e.topics);function rt(){const e=new Set;for(const t of Object.keys(F))e.add(t.split("/")[1]);return[...e]}function ct(e){const t=rt(),o=[],a=s=>{s&&!o.includes(s)&&o.push(s)};if(e){a(t.find(i=>i.toLowerCase()===e.toLowerCase()));const s=e.split("-")[0].toLowerCase();a(t.find(i=>i.toLowerCase()===s)),a(t.find(i=>i.toLowerCase().split("-")[0]===s))}return a(de),o}function dt(e,t){var l;const o=t.replace(/\r\n?/g,`
`).split(`
`);let a=e;const s=o.findIndex(d=>/^#\s+/.test(d));s>=0&&(a=o[s].replace(/^#\s+/,"").trim(),o.splice(s,1));const i=o.join(`
`).trim(),h=((l=i.split(/\n\s*\n/).map(d=>d.trim()).find(d=>d&&!/^(#|!\[|>|-|\d+\.|\|)/.test(d)))==null?void 0:l.replace(/\s+/g," "))??"",r=U(`${a}
${i}`).replace(/^\s*(#+|\||>|-|\d+\.)\s*/gm,"").replace(/\||:?-{3,}:?/g," ").replace(/\s+/g," ");return{id:e,title:a,summary:U(h),body:i,plainText:r,searchText:r.toLowerCase()}}function U(e){return e.replace(/!\[([^\]]*)\]\([^)]*\)/g,"$1").replace(/\[([^\]]+)\]\([^)]*\)/g,"$1").replace(/\*\*|`|^>\s*\[![A-Z]+\]/gm,"").replace(/(^|\s)\*([^*]+)\*/g,"$1$2")}const $=new Map;function lt(e){const t=e??"",o=$.get(t);if(o)return o;const a=ct(e),s=[];for(const i of it){const h=a.find(r=>F[`./${r}/${i}.md`]!==void 0);h&&s.push(dt(i,F[`./${h}/${i}.md`]))}return $.set(t,s),s}function ht(e){return st[`./images/${e.replace(/^\.?\/?(images\/)?/,"")}`]}function pt(e){return U(e).toLowerCase().replace(/[^a-z0-9]+/g,"-").replace(/^-+|-+$/g,"")}function ae(e,t){const o=t.toLowerCase().split(/\s+/).filter(Boolean);return o.length===0?[]:e.filter(a=>o.every(s=>a.searchText.includes(s))).map(a=>{const s=o.every(i=>a.title.toLowerCase().includes(i));return{topic:a,snippet:ut(a,o[0]),score:s?0:1}}).sort((a,s)=>a.score-s.score).map(({topic:a,snippet:s})=>({topic:a,snippet:s}))}function ut(e,t){const o=e.plainText,a=e.searchText.indexOf(t);if(a<0)return e.summary;const s=Math.max(0,a-60),i=Math.min(o.length,a+t.length+90);return`${s>0?"…":""}${o.slice(s,i).trim()}${i<o.length?"…":""}`}const mt=P(n.jsx("path",{d:"M19 5v14H5V5zm0-2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2m-4.86 8.86-3 3.87L9 13.14 6 17h12z"}),"ImageOutlined"),gt=P(n.jsx("path",{d:"M9 21c0 .55.45 1 1 1h4c.55 0 1-.45 1-1v-1H9zm3-19C8.14 2 5 5.14 5 9c0 2.38 1.19 4.47 3 5.74V17c0 .55.45 1 1 1h6c.55 0 1-.45 1-1v-2.26c1.81-1.27 3-3.36 3-5.74 0-3.86-3.14-7-7-7m2.85 11.1-.85.6V16h-4v-2.3l-.85-.6C7.8 12.16 7 10.63 7 9c0-2.76 2.24-5 5-5s5 2.24 5 5c0 1.63-.8 3.16-2.15 4.1"}),"LightbulbOutlined"),oe=/^(#{1,6})\s+(.*)$/,se=/^\s*(-{3,}|\*{3,}|_{3,})\s*$/,ie=/^!\[([^\]]*)\]\(([^)\s]+)\)\s*$/,O=/^(\s*)([-*+]|\d+[.)])\s+(.*)$/,E=e=>e.match(/^\s*/)[0].length;function Q(e){return oe.test(e)||se.test(e)||ie.test(e)||/^\s*>/.test(e)||/^\s*\|/.test(e)||O.test(e)}function D(e){return e.trim().replace(/^\|/,"").replace(/\|$/,"").split(new RegExp("(?<!\\\\)\\|")).map(t=>t.trim().replace(/\\\|/g,"|"))}function R(e){var s;const t=e.replace(/\r\n?/g,`
`).split(`
`),o=[];let a=0;for(;a<t.length;){const i=t[a];if(!i.trim()){a++;continue}const h=i.match(oe);if(h){const c=h[2].trim();o.push({kind:"heading",level:h[1].length,text:c,id:pt(c)}),a++;continue}if(se.test(i)){o.push({kind:"hr"}),a++;continue}const r=i.match(ie);if(r){o.push({kind:"image",alt:r[1],src:r[2]}),a++;continue}if(/^\s*>/.test(i)){const c=[];for(;a<t.length&&/^\s*>/.test(t[a]);)c.push(t[a++].replace(/^\s*>\s?/,""));const g=(s=c[0])==null?void 0:s.match(/^\[!(TIP|NOTE|WARNING|IMPORTANT|CAUTION)\]\s*$/i),u=g?g[1].toUpperCase()==="TIP"?"tip":["WARNING","CAUTION"].includes(g[1].toUpperCase())?"warning":"note":"quote";o.push({kind:"callout",variant:u,blocks:R((g?c.slice(1):c).join(`
`))});continue}if(/^\s*\|/.test(i)){const c=[];for(;a<t.length&&/^\s*\|/.test(t[a]);)c.push(t[a++]);const g=D(c[0]),u=c.length>1&&/^[\s|:-]+$/.test(c[1]),w=u?D(c[1]).map(m=>m.startsWith(":")&&m.endsWith(":")?"center":m.endsWith(":")?"right":void 0):[];o.push({kind:"table",header:g,align:w,rows:c.slice(u?2:1).map(D)});continue}const l=i.match(O);if(l){const c=l[1].length,g=/\d/.test(l[2]),u=[];for(;a<t.length;){const w=t[a],m=w.match(O);if(m&&m[1].length===c&&/\d/.test(m[2])===g){u.push([m[3]]),a++;continue}if(!w.trim()){let v=a+1;for(;v<t.length&&!t[v].trim();)v++;const k=t[v],S=k==null?void 0:k.match(O);if(!(k!==void 0&&(S&&S[1].length===c&&/\d/.test(S[2])===g||E(k)>c)))break;u[u.length-1].push(""),a++;continue}if(E(w)>c){u[u.length-1].push(w),a++;continue}if(!Q(w)){u[u.length-1].push(w),a++;continue}break}o.push({kind:"list",ordered:g,start:g&&parseInt(l[2],10)||1,items:u.map(w=>{const[m,...v]=w,k=Math.min(...v.filter(A=>A.trim()).map(E),1/0),S=v.map(A=>A.trim()?A.slice(Number.isFinite(k)?k:0):"");return R([m,...S].join(`
`))})});continue}const d=[];for(;a<t.length&&t[a].trim()&&(d.length===0||!Q(t[a]));)d.push(t[a++].trim());o.push({kind:"paragraph",text:d.join(" ")})}return o}const yt=new RegExp("`([^`]+)`|\\*\\*(.+?)\\*\\*|\\[([^\\]]+)\\]\\(([^)\\s]+)\\)|(?<![\\w*])\\*(?![\\s*])(.+?)\\*(?![\\w*])");function C(e){const t=[];let o=e,a=0;for(;o;){const s=o.match(yt);if(!s||s.index===void 0){t.push(o);break}s.index>0&&t.push(o.slice(0,s.index)),s[1]!==void 0?t.push(n.jsx(p,{component:"code",sx:{fontFamily:"monospace",fontSize:"0.92em",bgcolor:"action.hover",px:.5,py:.1,borderRadius:.5},children:s[1]},a++)):s[2]!==void 0?t.push(n.jsx("strong",{children:C(s[2])},a++)):s[3]!==void 0?t.push(n.jsx(wt,{href:s[4],children:C(s[3])},a++)):t.push(n.jsx("em",{children:C(s[5])},a++)),o=o.slice(s.index+s[0].length)}return t}function wt({href:e,children:t}){if(e.startsWith("help:")){const[o,a]=e.slice(5).split("#");return n.jsx(T,{component:b,to:{pathname:`/help/${o}`,hash:a?`#${a}`:""},children:t})}return e.startsWith("#")?n.jsx(T,{component:b,to:{hash:e},children:t}):e.startsWith("/")?n.jsx(T,{component:b,to:e,children:t}):n.jsx(T,{href:e,target:"_blank",rel:"noopener noreferrer",children:t})}function ft({alt:e,src:t}){const{t:o}=x(),[a,s]=f.useState(!1),i=/^https?:/.test(t)?t:ht(t);return n.jsxs(p,{component:"figure",sx:{mx:0,my:2.5},children:[i?n.jsx(p,{component:"img",src:i,alt:e,title:o("help.enlargeImage"),onClick:()=>s(!0),sx:{display:"block",maxWidth:"100%",maxHeight:560,borderRadius:1,border:1,borderColor:"divider",boxShadow:1,cursor:"zoom-in"}}):n.jsxs(p,{sx:{display:"flex",alignItems:"center",justifyContent:"center",gap:1,minHeight:120,border:2,borderStyle:"dashed",borderColor:"divider",borderRadius:1,color:"text.secondary",px:2},children:[n.jsx(mt,{}),n.jsx(y,{variant:"body2",children:o("help.screenshotComingSoon")})]}),e&&n.jsx(y,{component:"figcaption",variant:"caption",color:"text.secondary",sx:{display:"block",mt:.75},children:e}),i&&n.jsxs(me,{open:a,onClose:()=>s(!1),maxWidth:"xl",children:[n.jsx(ge,{"aria-label":o("common.actions.close"),onClick:()=>s(!1),sx:{position:"absolute",right:8,top:8,bgcolor:"background.paper","&:hover":{bgcolor:"background.paper"}},children:n.jsx(ye,{})}),n.jsx(we,{sx:{p:1},children:n.jsx(p,{component:"img",src:i,alt:e,sx:{display:"block",maxWidth:"100%"}})})]})]})}function bt({variant:e,blocks:t}){const{t:o}=x();if(e==="quote")return n.jsx(p,{sx:{borderLeft:4,borderColor:"divider",pl:2,my:2,color:"text.secondary"},children:n.jsx(L,{blocks:t})});const a=e==="tip"?"success":e==="warning"?"warning":"info";return n.jsxs(B,{severity:a,icon:e==="tip"?n.jsx(gt,{fontSize:"inherit"}):void 0,sx:{my:2,"& p:last-child, & ul:last-child, & ol:last-child":{mb:0}},children:[n.jsx(fe,{children:o(`help.callout.${e}`)}),n.jsx(L,{blocks:t})]})}function kt({block:e}){switch(e.kind){case"heading":{const t=e.level<=1?"h4":e.level===2?"h5":e.level===3?"h6":"subtitle1";return n.jsx(y,{id:e.id,variant:t,component:`h${Math.min(e.level,6)}`,sx:{mt:e.level<=2?4:3,mb:1.25,scrollMarginTop:72,fontWeight:e.level>=4?600:void 0},children:C(e.text)})}case"paragraph":return n.jsx(y,{variant:"body1",sx:{mb:1.5,lineHeight:1.7},children:C(e.text)});case"list":return n.jsx(p,{component:e.ordered?"ol":"ul",start:e.ordered?e.start:void 0,sx:{pl:3,mt:0,mb:1.5,"& > li":{mb:.5,lineHeight:1.7},"& li > p":{mb:.5}},children:e.items.map((t,o)=>n.jsx(y,{component:"li",variant:"body1",children:t.length===1&&t[0].kind==="paragraph"?C(t[0].text):n.jsx(L,{blocks:t})},o))});case"callout":return n.jsx(bt,{variant:e.variant,blocks:e.blocks});case"table":return n.jsx(Fe,{component:j,variant:"outlined",sx:{my:2},children:n.jsxs(he,{size:"small",children:[n.jsx(pe,{children:n.jsx(H,{children:e.header.map((t,o)=>n.jsx(z,{align:e.align[o],sx:{fontWeight:600,whiteSpace:"nowrap"},children:C(t)},o))})}),n.jsx(ue,{children:e.rows.map((t,o)=>n.jsx(H,{children:t.map((a,s)=>n.jsx(z,{align:e.align[s],children:C(a)},s))},o))})]})});case"image":return n.jsx(ft,{alt:e.alt,src:e.src});case"hr":return n.jsx(le,{sx:{my:3}})}}function L({blocks:e}){return n.jsx(n.Fragment,{children:e.map((t,o)=>n.jsx(f.Fragment,{children:n.jsx(kt,{block:t})},o))})}function vt({blocks:e}){return n.jsx(p,{sx:{"& > :first-child":{mt:0}},children:n.jsx(L,{blocks:e})})}function At(){const{t:e,i18n:t}=x(),{topicId:o}=be(),a=f.useMemo(()=>lt(t.language),[t.language]),s=f.useMemo(()=>new Map(a.map(l=>[l.id,l])),[a]),[i,h]=f.useState("");if(!o)return n.jsx(Ct,{topics:a,byId:s,query:i,setQuery:h});const r=s.get(o);return n.jsxs(p,{sx:{display:"flex",gap:3,alignItems:"flex-start"},children:[n.jsx(p,{sx:{display:{xs:"none",md:"block"},width:240,flexShrink:0,position:"sticky",top:64},children:n.jsx(xt,{topics:a,byId:s,current:o,query:i,setQuery:h})}),n.jsxs(p,{sx:{flex:1,minWidth:0},children:[n.jsx(_,{component:b,to:"/help",size:"small",startIcon:n.jsx(ne,{}),sx:{display:{md:"none"},mb:1},children:e("help.allTopics")}),r?n.jsx(Tt,{topic:r,topics:a}):n.jsx(B,{severity:"warning",action:n.jsx(_,{component:b,to:"/help",children:e("help.allTopics")}),children:e("help.topicNotFound")})]})]})}function re({query:e,setQuery:t,autoFocus:o}){const{t:a}=x();return n.jsx(Ae,{size:"small",fullWidth:!0,autoFocus:o,placeholder:a("help.searchPlaceholder"),value:e,onChange:s=>t(s.target.value),slotProps:{input:{startAdornment:n.jsx(Ie,{position:"start",children:n.jsx(_e,{fontSize:"small"})})}}})}function Ct({topics:e,byId:t,query:o,setQuery:a}){const{t:s}=x(),i=f.useMemo(()=>ae(e,o),[e,o]),h=o.trim().length>0;return n.jsxs(I,{spacing:3,sx:{maxWidth:1100},children:[n.jsxs(p,{children:[n.jsx(y,{variant:"h4",gutterBottom:!0,children:s("help.title")}),n.jsx(y,{color:"text.secondary",children:s("help.subtitle")})]}),n.jsx(p,{sx:{maxWidth:560},children:n.jsx(re,{query:o,setQuery:a,autoFocus:!0})}),h?n.jsxs(I,{spacing:1.5,children:[n.jsx(y,{variant:"overline",color:"text.secondary",children:s("help.resultCount",{count:i.length})}),i.length===0&&n.jsx(B,{severity:"info",children:s("help.noResults",{query:o.trim()})}),i.map(({topic:r,snippet:l})=>n.jsx(ke,{variant:"outlined",children:n.jsx(Me,{component:b,to:`/help/${r.id}`,children:n.jsxs(ve,{children:[n.jsx(y,{variant:"h6",children:r.title}),n.jsx(y,{variant:"body2",color:"text.secondary",children:l})]})})},r.id))]}):n.jsxs(n.Fragment,{children:[t.has("getting-started")&&n.jsx(B,{severity:"info",action:n.jsx(_,{component:b,to:"/help/getting-started",endIcon:n.jsx(te,{}),children:s("help.startHereAction")}),children:s("help.startHere")}),n.jsx(p,{sx:{display:"grid",gap:2,gridTemplateColumns:{xs:"1fr",sm:"repeat(2, 1fr)",lg:"repeat(3, 1fr)"}},children:G.map(r=>{const l=r.topics.map(d=>t.get(d)).filter(d=>!!d);return l.length===0?null:n.jsxs(j,{variant:"outlined",sx:{p:2},children:[n.jsx(y,{variant:"overline",color:"primary",sx:{fontWeight:600},children:s(`help.groups.${r.key}`)}),n.jsx(I,{spacing:1.5,sx:{mt:.5},children:l.map(d=>n.jsxs(p,{children:[n.jsx(T,{component:b,to:`/help/${d.id}`,variant:"subtitle1",underline:"hover",sx:{fontWeight:600},children:d.title}),n.jsx(y,{variant:"body2",color:"text.secondary",children:d.summary})]},d.id))})]},r.key)})})]})]})}function xt({topics:e,byId:t,current:o,query:a,setQuery:s}){const{t:i}=x(),h=f.useMemo(()=>a.trim()?new Set(ae(e,a).map(r=>r.topic.id)):void 0,[e,a]);return n.jsxs(j,{variant:"outlined",sx:{maxHeight:"calc(100vh - 88px)",overflowY:"auto"},children:[n.jsx(p,{sx:{p:1.5,pb:.5},children:n.jsx(re,{query:a,setQuery:s})}),n.jsxs(Ce,{dense:!0,disablePadding:!0,children:[n.jsx(V,{component:b,to:"/help",children:n.jsx(q,{primary:i("help.allTopics"),slotProps:{primary:{color:"primary",fontWeight:600}}})}),G.map(r=>{const l=r.topics.map(d=>t.get(d)).filter(d=>!!d&&(!h||h.has(d.id)));return l.length===0?null:n.jsx("li",{children:n.jsxs("ul",{style:{padding:0},children:[n.jsx(xe,{sx:{lineHeight:"32px",bgcolor:"background.paper"},children:i(`help.groups.${r.key}`)}),l.map(d=>n.jsx(V,{component:b,to:`/help/${d.id}`,selected:d.id===o,sx:{pl:3},children:n.jsx(q,{primary:d.title})},d.id))]})},r.key)}),h&&h.size===0&&n.jsx(y,{variant:"body2",color:"text.secondary",sx:{px:2,py:1},children:i("help.noResults",{query:a.trim()})})]})]})}function Tt({topic:e,topics:t}){const{t:o}=x(),a=Te(),s=Se(),i=f.useMemo(()=>R(e.body),[e]),h=i.filter(c=>c.kind==="heading"&&c.level===2),r=t.findIndex(c=>c.id===e.id),l=r>0?t[r-1]:void 0,d=r>=0&&r<t.length-1?t[r+1]:void 0;return f.useEffect(()=>{const c=decodeURIComponent(a.hash.replace(/^#/,""));if(!c){window.scrollTo({top:0});return}let g=!1;const u=()=>{var m;g||(m=document.getElementById(c))==null||m.scrollIntoView({behavior:"smooth",block:"start"})};u();const w=[...document.querySelectorAll("article img")].filter(m=>!m.complete);return w.length&&Promise.all(w.map(m=>m.decode().catch(()=>{}))).then(u),()=>{g=!0}},[a.hash,e.id]),n.jsxs(p,{sx:{display:"flex",gap:4,alignItems:"flex-start"},children:[n.jsxs(p,{component:"article",sx:{flex:1,minWidth:0,maxWidth:880},children:[n.jsx(y,{variant:"h4",component:"h1",gutterBottom:!0,children:e.title}),n.jsx(vt,{blocks:i}),n.jsxs(I,{direction:"row",spacing:2,sx:{mt:5,pt:2,borderTop:1,borderColor:"divider"},justifyContent:"space-between",children:[l?n.jsx(_,{startIcon:n.jsx(ne,{}),onClick:()=>s(`/help/${l.id}`),sx:{textAlign:"left"},children:n.jsxs(p,{children:[n.jsx(y,{variant:"caption",display:"block",color:"text.secondary",children:o("help.previous")}),l.title]})}):n.jsx("span",{}),d&&n.jsx(_,{endIcon:n.jsx(te,{}),onClick:()=>s(`/help/${d.id}`),sx:{textAlign:"right"},children:n.jsxs(p,{children:[n.jsx(y,{variant:"caption",display:"block",color:"text.secondary",children:o("help.next")}),d.title]})})]})]}),h.length>1&&n.jsxs(p,{component:"nav",sx:{display:{xs:"none",lg:"block"},width:220,flexShrink:0,position:"sticky",top:64},children:[n.jsx(y,{variant:"overline",color:"text.secondary",children:o("help.onThisPage")}),n.jsx(I,{spacing:.75,sx:{mt:.5,borderLeft:2,borderColor:"divider",pl:1.5},children:h.map(c=>n.jsx(T,{component:b,to:{hash:`#${c.id}`},variant:"body2",underline:"hover",color:a.hash===`#${c.id}`?"primary":"text.secondary",children:c.text.replace(/\*\*|`/g,"")},c.id))})]})]})}export{At as HelpPage};
