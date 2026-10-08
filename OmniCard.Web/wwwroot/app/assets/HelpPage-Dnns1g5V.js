import{g as J,a as K,r as f,u as X,b as Y,c as N,j as n,d as Z,s as W,B as ce,m as ee,e as E,F as de,f as p,D as le,P as j,T as he,h as pe,i as H,k as V,l as ue,n as y,o as x,p as me,I as ge,C as ye,q as we,A as B,t as fe,L as T,v as b,w as be,x as _,S as I,y as ke,z as ve,E as Ce,G as z,H as q,J as xe,K as Te,M as Se,N as Ae,O as Ie,Q as _e}from"./index-B3Kdn6dR.js";function Oe(e){return J("MuiCardActionArea",e)}const P=K("MuiCardActionArea",["root","focusVisible","focusHighlight"]),Be=e=>{const{classes:t}=e;return Z({root:["root"],focusHighlight:["focusHighlight"]},Oe,t)},Le=W(ce,{name:"MuiCardActionArea",slot:"Root",overridesResolver:(e,t)=>t.root})(ee(({theme:e})=>({display:"block",textAlign:"inherit",borderRadius:"inherit",width:"100%",[`&:hover .${P.focusHighlight}`]:{opacity:(e.vars||e).palette.action.hoverOpacity,"@media (hover: none)":{opacity:0}},[`&.${P.focusVisible} .${P.focusHighlight}`]:{opacity:(e.vars||e).palette.action.focusOpacity}}))),Ee=W("span",{name:"MuiCardActionArea",slot:"FocusHighlight",overridesResolver:(e,t)=>t.focusHighlight})(ee(({theme:e})=>({overflow:"hidden",pointerEvents:"none",position:"absolute",top:0,right:0,bottom:0,left:0,borderRadius:"inherit",opacity:0,backgroundColor:"currentcolor",transition:e.transitions.create("opacity",{duration:e.transitions.duration.short})}))),Pe=f.forwardRef(function(t,o){const a=X({props:t,name:"MuiCardActionArea"}),{children:r,className:s,focusVisibleClassName:h,slots:i={},slotProps:l={},...d}=a,c=a,g=Be(c),u={slots:i,slotProps:l},[w,m]=Y("root",{elementType:Le,externalForwardedProps:{...u,...d},shouldForwardComponentProp:!0,ownerState:c,ref:o,className:N(g.root,s),additionalProps:{focusVisibleClassName:N(h,g.focusVisible)}}),[v,k]=Y("focusHighlight",{elementType:Ee,externalForwardedProps:u,ownerState:c,ref:o,className:g.focusHighlight});return n.jsxs(w,{...m,children:[r,n.jsx(v,{...k})]})});function Me(e){return J("MuiTableContainer",e)}K("MuiTableContainer",["root"]);const De=e=>{const{classes:t}=e;return Z({root:["root"]},Me,t)},Ne=W("div",{name:"MuiTableContainer",slot:"Root",overridesResolver:(e,t)=>t.root})({width:"100%",overflowX:"auto"}),Fe=f.forwardRef(function(t,o){const a=X({props:t,name:"MuiTableContainer"}),{className:r,component:s="div",...h}=a,i={...a,component:s},l=De(i);return n.jsx(Ne,{ref:o,as:s,className:N(l.root,r),ownerState:i,...h})}),ne=E(n.jsx("path",{d:"M20 11H7.83l5.59-5.59L12 4l-8 8 8 8 1.41-1.41L7.83 13H20z"}),"ArrowBack"),te=E(n.jsx("path",{d:"m12 4-1.41 1.41L16.17 11H4v2h12.17l-5.58 5.59L12 20l8-8z"}),"ArrowForward"),Ue=`# Administration\r
\r
The Administration page is where you change your own password and, depending on your access, manage users, roles, sites, catalog data and OmniCard's settings. This topic explains every tab and who can see it.\r
\r
## Open Administration\r
\r
Click **Administration** in the sidebar. Everyone can open this page, but you only see the tabs your account allows. Administrators can also open **Account ▸ Account & users** from the top bar to jump straight to the **Users** tab.\r
\r
![The Administration page with its row of tabs](administration-tabs.png)\r
\r
| Tab | What it's for | Who sees it |\r
|---|---|---|\r
| **Sales** | Where picked sale cards are moved | People with the Settings view permission |\r
| **Receipts** | Store details and layout for printed receipts | People with the Settings view permission (only administrators can change it) |\r
| **Scan Badges** | Value badges on the Scan page, plus watched scan folders | People with the Settings view permission (badges and folders can only be changed by administrators) |\r
| **Deck Types** | Deck formats and their rules | People with the Deck Types view permission |\r
| **Appearance** | Card preview size | Everyone |\r
| **Catalog Data** | Download card catalogs, prices and artwork | People with the Catalog Data view permission |\r
| **eBay** | Connect eBay and set seller details | People with the eBay view permission |\r
| **Roles** | Permission bundles | Administrators only |\r
| **Sites** | Major physical places and who can see them | Administrators only |\r
| **Users** | Your password, and (for administrators) all accounts | Everyone |\r
| **Components** | Software versions and licenses | Everyone |\r
\r
If the page scrolls sideways on a small screen, use the arrows at either end of the tab row to see more tabs.\r
\r
## Change your password\r
\r
Anyone can change their own password.\r
\r
1. Go to **Administration ▸ Users**.\r
2. Under **Your password**, enter your **Current password**.\r
3. Enter a **New password**, then type it again in **Confirm new password**.\r
4. Click **Change password**. You'll see *Password changed.*\r
\r
If the two new passwords don't match, the form says *Passwords don't match.* If your current password is wrong, you'll see *Current password is incorrect.* If you've forgotten your password, ask an administrator to reset it.\r
\r
## Users\r
\r
Administrators see a list of every account below **Your password**, with each person's **Username** and **Role**.\r
\r
- **system** marks the built-in Admin account. It can't be deleted and always has full access, but you can reset its password.\r
- **custom** means the person has extra permissions given or taken away on top of their role.\r
\r
![The Users tab with the list of accounts](administration-users.png)\r
\r
### Add a user\r
\r
1. Click **Add user**.\r
2. Enter a **Username**, a **Password** and **Confirm password**.\r
3. Choose a **Role**. If you leave it as **No role**, the new user gets the *Viewer* role, which can look at everything but change nothing.\r
4. Tick **Administrator (full access)** instead if this person should be able to do everything, including managing users.\r
5. Click **Create**.\r
\r
### Change what a user can do\r
\r
1. Click the **Edit access** button (pencil) on the user's row.\r
2. Tick or untick **Administrator (full access)**. Administrators skip roles and permissions entirely.\r
3. For everyone else, choose a **Role**.\r
4. Under **Also allow (grant)**, tick extra permissions this person should have beyond their role.\r
5. Under **Never allow (deny)**, tick permissions to take away from this person even if their role includes them.\r
6. Click **Save**.\r
\r
Changes take effect straight away. The person doesn't need to sign out and back in. Their sidebar updates the next time they move to another page.\r
\r
### Reset a password\r
\r
1. Click the **Reset password** button (key) on the user's row.\r
2. Enter the new **Password** and **Confirm password**.\r
3. Click **Reset**, then tell the person their new password. They can change it themselves afterwards.\r
\r
### Delete a user\r
\r
Click the **Delete user** button (bin) on the row and confirm. The built-in Admin account can't be deleted.\r
\r
## Roles\r
\r
A role is a reusable bundle of permissions you assign to users. Only administrators see this tab.\r
\r
OmniCard comes with three built-in roles. You can change their permissions, but you can't rename or delete them.\r
\r
| Role | What it allows |\r
|---|---|\r
| **Administrator** | Every permission |\r
| **Viewer** | Looking at every section, without changing anything. New users get this role by default |\r
| **Staff** | Everyday work: viewing everything, scanning, editing cards, creating locations, lists, trades, imports, orders and listings. No deleting and no settings |\r
\r
### Add or edit a role\r
\r
1. Click **Add role**, or the **Edit** button (pencil) on a role's row.\r
2. Enter a **Name**. Built-in roles keep their name.\r
3. Tick the permissions the role should have. Ticking a section's heading ticks every permission in that section.\r
4. Click **Save**.\r
\r
To delete a role you created, click its **Delete** button (bin). People who had that role keep any permissions given to them personally, but lose the role's permissions.\r
\r
### The permissions\r
\r
Permissions are grouped by section. Most sections have **View**, plus actions such as **Create**, **Edit** and **Delete**.\r
\r
| Section | Permissions |\r
|---|---|\r
| Dashboard | View |\r
| Scan | View, Commit scans |\r
| Collection | View, Edit, Delete, Export |\r
| Locations | View, Create, Edit, Delete |\r
| Binder | View, Edit |\r
| Sets | View, Export want list |\r
| Inventory | View, Create, Edit, Delete |\r
| Lists | View, Create, Edit, Delete, Commit to collection |\r
| Trades | View, Create, Finalize, Cancel |\r
| Import | Run import |\r
| Export | Run export |\r
| Sales · Orders | View, Create, Edit, Delete, Import orders |\r
| Sales · Customers | View, Create, Edit, Delete |\r
| Sales · Listings | View, Create, Edit, Delete, Pick / mark picked |\r
| Settings | View, Edit |\r
| Deck Types | View, Edit |\r
| Catalog Data | View, Refresh |\r
| eBay | View, Manage / connect |\r
\r
Managing users, roles and sites isn't a permission. Only administrators can do it.\r
\r
## Sites\r
\r
A site is a major physical place, such as a home, a shop or a storage unit, that holds many locations. Use sites when several people share one OmniCard and shouldn't all see everything. Only administrators see this tab.\r
\r
The **Default** site is always visible to every user and holds every location that isn't assigned elsewhere, including Bulk. It can be renamed but not deleted, and it has no access list.\r
\r
### Add or edit a site\r
\r
1. Click **Add site**, or the **Edit** button (pencil) on a site's row.\r
2. Enter a **Name**, for example "The Shop", and an optional **Description**.\r
3. Click **Save**.\r
\r
New locations are created in a site from the [Locations](help:locations) page. You can also move an existing location to another site there.\r
\r
### Choose who can see a site\r
\r
1. Click the **Who can see this site** button (people icon) on the site's row.\r
2. For each role and each user, choose **None**, **Read** or **Write**:\r
   - **Read** lets them browse and search the site's locations and cards, and add those cards to lists.\r
   - **Write** also lets them change those locations and cards, as far as their normal permissions allow.\r
3. Click **Save**.\r
\r
A person gets the higher of their own level and their role's level. Administrators always see every site.\r
\r
![Choosing who can see a site, with None, Read and Write for each role and user](administration-site-access.png)\r
\r
### Delete a site\r
\r
1. Click the **Delete** button (bin) on the site's row.\r
2. If the site has locations, choose a site under **Move its locations to**. Its locations and all their cards move there.\r
3. Click **Delete**.\r
\r
## Catalog Data\r
\r
OmniCard recognizes and prices cards using a catalog for each game, stored on the server. This tab refreshes those catalogs. You need the Catalog Data view permission to see it, and the Refresh permission to run anything.\r
\r
![The Catalog Data tab with a download in progress](administration-catalog-data.png)\r
\r
### Refresh a catalog\r
\r
1. Choose a **Game**.\r
2. Click one of the jobs:\r
   - **Download catalog** gets the latest card list, including new sets. Run this first on a new server, and again when a new set comes out.\r
   - **Update prices** refreshes market prices.\r
   - **Recompute hashes** rebuilds the image fingerprints OmniCard uses to recognize scanned cards. It only processes cards that don't have one yet. **Download catalog** already does this for new cards, so you'll rarely need it on its own.\r
   - **Download artwork** saves a copy of every card image on the server, so pictures load quickly and don't depend on outside websites.\r
3. Watch the progress message. When it finishes, the job appears under **Recent** with ✓ if it worked or ✗ and an error message if it didn't.\r
\r
Only one job runs at a time, for all games. While a job is running, the buttons are greyed out. Large catalogs, especially Magic: The Gathering, can take a long time.\r
\r
> [!NOTE]\r
> A job that finishes very quickly hasn't necessarily failed. If nothing has changed since the last run, there's little to do. Check the ✓ under **Recent**.\r
\r
### Languages to download\r
\r
Under the buttons, **Languages to download** controls which non-English printings the chosen game's catalog includes. Downloading them lets scans of foreign cards match their own artwork.\r
\r
- **English** is always included.\r
- Tick or untick languages. Your choice saves straight away, but only takes effect the next time you click **Download catalog**. Unticked languages are removed from the catalog then.\r
- For Magic: The Gathering, adding any non-English language switches to a much larger download (about 400 MB).\r
- Non-English cards rarely have their own prices, so they're valued at the English printing's price. Japanese Pokémon cards are the exception: they have real prices.\r
- Some games' sources only provide English cards. For those games you'll see a note instead of checkboxes. You can still record a foreign copy's language when you scan or edit it.\r
\r
## Scan Badges\r
\r
This tab has two parts: value badges on the Scan page, and watched scan folders.\r
\r
### Scan badges\r
\r
On the Scan page, matched cards show a gold star when the card isn't in your collection yet, and one to five currency signs showing how valuable it is. Here you set how those signs are worked out. Only administrators can change these settings.\r
\r
1. Enter the **Currency code (ISO 4217)**, for example \`USD\`, \`EUR\` or \`GBP\`. This picks the currency sign.\r
2. Fill in **Tier 1 max** to **Tier 4 max**. A card worth up to the tier 1 amount shows one sign, up to the tier 2 amount shows two, and so on. Anything above tier 4 shows five.\r
3. Check the **Preview**, which lists the price range for each number of signs.\r
4. Click **Save**.\r
\r
The amounts should go up from tier 1 to tier 4. If they don't, OmniCard warns you and sorts them when you save. See [Scanning cards](help:scanning).\r
\r
### Watched scan folders\r
\r
Only administrators see this part. It points OmniCard at folders on the server where a document scanner saves its images. Each subfolder becomes a scan batch that's matched in the background and waits for review on the Scan page. See [Scan batches](help:scan-batches).\r
\r
![The Watched scan folders settings for each game](administration-scan-folders.png)\r
\r
1. Turn on **Watch folders**.\r
2. Set the **Quiet period (seconds)**. This is how long OmniCard waits after the last new file arrives before it starts matching a batch.\r
3. Set **Keep images (days)**. Stored scans of batches that were added or discarded are deleted after this many days.\r
4. For each game you scan, enter the **Folder on the server**, for example \`D:\\Scans\\Mtg\`, and make sure **Active** is on.\r
5. Once a folder is entered, choose the defaults for that game's batches: **Sets (art fallback)**, **Condition**, **Card language** and **Foil**.\r
6. Optionally, click **Choose…** next to **Default location** to pre-select where that game's batches are added. **Clear** removes it.\r
7. Click **Save**.\r
\r
A chip next to each folder shows its status:\r
\r
| Status | Meaning |\r
|---|---|\r
| **Found** | The folder exists and OmniCard can use it |\r
| **Folder not found** | The path is wrong, or the server can't reach it |\r
| **Can't move files** | OmniCard can read the folder but can't move processed files into its \`_processed\` subfolder. Check the folder's permissions |\r
\r
Save each batch into its own subfolder, named after the batch. When OmniCard picks up a file, it moves the original into a \`_processed\` subfolder.\r
\r
> [!NOTE]\r
> Folders are only watched while the OmniCard server is running. If batches only appear after someone opens OmniCard in a browser, ask whoever runs the server to keep it always running.\r
\r
## Receipts\r
\r
Set up the receipts you print for sales orders. They're sized for thermal or roll printers. You print a receipt from an order's detail panel on the [Sales](help:sales) page. Only administrators can change these settings.\r
\r
1. Under **Company / store details**, fill in your **Store name**, **Email**, **Phone** and address.\r
2. Under **Logo**, click **Upload logo…** to add your logo, or **Remove logo** to take it off. PNG, JPG, GIF, WEBP and BMP images up to 5 MB are accepted.\r
3. Under **Printer layout**, set the **Paper width (mm)**, or click the **58 mm** or **80 mm** shortcut. Then set the **Margin (mm)** and **Font size (pt)**.\r
4. Turn **Show prices** off if you don't want prices on the receipt, for example for gift receipts.\r
5. Add any **Footer text**, for example "Thanks for your business! All sales final."\r
6. Check the preview, which is drawn at roughly the paper width you chose.\r
7. Click **Save**.\r
\r
## Sales\r
\r
This tab controls what happens when you mark a listing as picked on the Sales page. See [Sales](help:sales).\r
\r
- **For-sale location** is where picked cards are moved, for example a "For sale" box. Click **Change** to pick a location, or **Clear** to remove it.\r
- **Move picked cards to the for-sale location** turns the automatic move on or off. When it's off, marking a listing as picked only changes its status and the card stays where it is.\r
\r
Changing these settings needs the Settings edit permission.\r
\r
## Deck Types\r
\r
Deck types are the formats a deck box can be assigned, such as Commander or Standard. They're set up per game. Their rules only produce warnings in a deck box. They never stop you adding cards. See [Deck boxes](help:deck-boxes).\r
\r
1. Choose a **Game**.\r
2. The table lists that game's deck types with their **Size**, **Copies** and **Commander** slots. Built-in types are marked **built-in**.\r
3. Click **Add deck type** to create your own, or the pencil on a row to edit one. The bin deletes it. Deck boxes that used a deleted type are left without a type.\r
\r
When adding or editing a deck type, you can set:\r
\r
| Field | What it means |\r
|---|---|\r
| **Name** | The format's name |\r
| **Deck size min** and **Deck size max** | How many cards the deck should have |\r
| **Max copies / card** | How many copies of one card are allowed |\r
| **Commander/leader slots** | How many commander or leader cards the format uses |\r
| **Singleton (max 1 of each card)** | Only one copy of each card is allowed |\r
| **Basic lands exempt from copy limit** | Basic lands don't count toward the copy limit |\r
| **Count copies by card number** | All the different artworks of the same card number count as one card |\r
\r
Changing deck types needs the Deck Types edit permission.\r
\r
## Appearance\r
\r
Choose how large the card picture grows when you hover over a card in a list. Drag the slider from 100% (the default) up to 300%. The text under the slider shows the popup's size in pixels.\r
\r
This setting is saved in your current browser only. It doesn't affect other people or your other devices.\r
\r
## eBay\r
\r
Connect OmniCard to your eBay seller account so you can list cards on eBay. See [eBay](help:ebay) for the full guide. You need the eBay view permission to see this tab, and the Manage / connect permission to change anything.\r
\r
### Connect your account\r
\r
The **Status** shows one of:\r
\r
- **Connected**: OmniCard is linked to your eBay account.\r
- **Not connected**: you can click **Connect to eBay**. You're sent to eBay to sign in and approve access, then brought back here.\r
- **Not configured**: the server is missing eBay app credentials. Whoever runs the server needs to add them first.\r
\r
When you're connected, **Run seller setup** creates your eBay inventory location and business policies from the seller settings below. **Disconnect** unlinks your account.\r
\r
### Seller settings\r
\r
Fill these in before you run seller setup.\r
\r
1. Under **Inventory location address**, enter at least **Address line 1**, **City**, **Postal code** and **Country (ISO-2, e.g. US)**. eBay needs a complete address.\r
2. Under **Shipping policy**, turn on **Free shipping** or enter a **Flat shipping cost**. Then set the **Handling time (days)** and choose a **Shipping service**.\r
3. Under **Return policy**, choose whether to **Accept returns**, the **Return window (days)**, and who pays return shipping (**Buyer** or **Seller**).\r
4. Click **Save seller settings**, then click **Run seller setup** above.\r
\r
**Setup status** shows whether the inventory location and the three business policies have been created, and when setup last completed.\r
\r
## Components\r
\r
This tab lists the software and third-party components that OmniCard ships with or runs on, grouped by category. Each row shows the **Version** and **License**, with links to the project's **Website** and **License**. Everyone can see it.\r
\r
## Related topics\r
\r
- [Getting started](help:getting-started): signing in and first-time setup\r
- [Troubleshooting](help:troubleshooting): missing sections, prices and images\r
- [Scan batches](help:scan-batches): reviewing scans from watched folders\r
- [Locations](help:locations): creating locations inside sites\r
- [eBay](help:ebay) and [Sales](help:sales): selling cards\r
`,Re=`# Binders\r
\r
The binder view shows a binder location the way it looks on your shelf: two facing pages of pockets, with each card in its own page and slot. Use it to flip through a binder, see what's for sale, and arrange cards into pockets.\r
\r
## Open a binder\r
\r
1. Go to [Locations](/locations).\r
2. In the **Binders** group, click the binder's name. Binders open straight into the binder view.\r
\r
You can also open a binder's regular location page and click **Open binder view**. To get back to the card table, click the binder's name in the page path at the top (**Locations ▸ *binder name* ▸ Binder**).\r
\r
Need a new binder? Create a location with the type **Binder**. See [Locations](help:locations#create-a-location).\r
\r
![A binder open to a two-page spread](binders-spread-view.webp)\r
\r
## Flip through the binder\r
\r
The binder is shown one spread (two facing pages) at a time, just like opening a real binder:\r
\r
- The first spread shows page 1 on its own, on the right.\r
- After that, each spread shows an even page on the left and the next odd page on the right (pages 2–3, 4–5, and so on).\r
\r
To move around:\r
\r
- Click **‹ Prev** or **Next ›** to turn one spread at a time.\r
- Click a tab in the page strip (labelled *1*, *2–3*, *4–5*…) to jump straight to that spread. On narrow screens the strip scrolls; use its arrows to see more tabs.\r
\r
The heading shows which pages you're looking at and how many pages the binder has, for example *Pages 2-3 · 12 pages*.\r
\r
## What the pockets show\r
\r
Each pocket shows the card's artwork. Hover over a card to see its name, condition and whether it's foil. Foil cards have a moving rainbow sheen so you can spot them at a glance.\r
\r
### Price and sale badges\r
\r
Small badges on each card tell you about its value and sale status:\r
\r
| Badge | Where | Meaning |\r
|---|---|---|\r
| Price | Bottom left | The card's current market price. |\r
| **Listed** (blue) | Top left | The card is listed for sale. |\r
| **Picked** (orange) | Top left | The card is listed and has been picked into your for-sale location. |\r
| Channel and price | Bottom right | Where it's listed (Manual, TCGplayer or eBay) and the listed price. |\r
\r
See [Sales](help:sales) for how listing and picking work.\r
\r
### Card backs in empty pockets\r
\r
A binder sheet has two sides, and each pocket on the front backs onto a pocket on the back. When a pocket is empty but the pocket behind it on the other side of the sheet holds a card, OmniCard shows that game's card back on a striped background. Hover over it to see *Card on the reverse side of this sheet*.\r
\r
This matches what you'd see through a clear pocket, and helps you recognize the page you're holding.\r
\r
## See a card's details\r
\r
Click any card to open its details. From there you can edit its condition, foil, quantity, purchase price, note and tags, list it for sale, add it to a trade, split a stack, move it to another location, or delete it. See [Collection](help:collection).\r
\r
You can also right-click a card and choose **Card details** or **List for sale**.\r
\r
## Arrange cards in edit mode\r
\r
Click **Edit** at the top right to start arranging the binder. The button changes to **Done editing**; click it when you're finished. Changes save as you make them.\r
\r
If you don't see **Edit**, the binder is in a site you can only view. If changes show an error, you may not have permission to edit binders. Either way, ask an administrator for access.\r
\r
![Edit mode with the Unplaced cards panel and edit toolbar](binders-edit-mode.webp)\r
\r
In edit mode you get:\r
\r
- An edit toolbar with **Add page**, **Layout** and **Remove page** buttons.\r
- The **Unplaced cards** panel on the left, listing cards that are in this binder but not in a pocket yet.\r
- Dashed pocket outlines highlighting where you can drop cards.\r
\r
### The Unplaced cards panel\r
\r
Cards end up in the Unplaced pool when you move or import them into a binder without choosing a pocket, when you split a stack, or when you remove a page.\r
\r
- The heading shows how many unplaced cards there are, for example *Unplaced cards (14)*.\r
- Each entry shows the card's set, number, condition, foil, market price and sale status.\r
- Use the search box to filter the list. It accepts the full search syntax, such as \`set:mh3\`, \`t:dragon\` or \`tag:trade\`. See [Search syntax](help:search-syntax).\r
- Click an entry to open its details, or right-click it for **Card details** and **List for sale**.\r
\r
When everything is placed, the panel says *Everything in this binder is placed.*\r
\r
### Place, move and remove cards\r
\r
- **Place a card**: drag it from the Unplaced cards panel onto a pocket.\r
- **Move a card**: drag it from one pocket to another, on the same spread.\r
- **Swap two cards**: drag a placed card onto an occupied pocket. The two cards trade places.\r
- **Replace a card from the pool**: drag an unplaced card onto an occupied pocket. The card that was there goes back to the Unplaced pool.\r
- **Remove a card from its pocket**: drag it from the pocket onto the Unplaced cards panel. The card stays in the binder; it just no longer has a pocket.\r
\r
> [!TIP]\r
> To move a card to a pocket on a different spread, drag it to the Unplaced cards panel first, turn to the spread you want, then drag it into the pocket.\r
\r
## Add or replace a card in a pocket\r
\r
You can fill a specific pocket with a card from anywhere in your collection, or with a brand-new card from the catalog.\r
\r
1. Right-click a pocket and choose **Add card to this pocket…** (or **Replace card in this pocket…** if it already holds a card).\r
2. The dialog shows the page and slot, and which card will be replaced.\r
3. Choose the **Game**, then search by **Name**, and optionally **Set** and **Collector #**.\r
4. Pick a result:\r
   - **In your collection** lists copies you own in other locations, with the location each is in. Click one to move a single copy into the pocket right away. If it's part of a stack, only one copy moves.\r
   - **In the catalog** lists every matching printing. Click one, set **Condition**, **Purchase price** and **Foil**, then click **Add to pocket** to add a new card to your collection in this pocket.\r
\r
![The Add card to pocket dialog](binders-add-to-pocket.png)\r
\r
If the pocket already held a card, that card goes back to the Unplaced pool.\r
\r
> [!NOTE]\r
> **In your collection** only lists copies in other locations. To place a copy that's already in this binder, drag it from the Unplaced cards panel.\r
\r
## Pages and layout\r
\r
### Add pages\r
\r
In edit mode, click **Add page** and choose:\r
\r
- **Double-sided sheet**: adds a sheet with a front and a back (two pages).\r
- **Single-sided page**: adds a sheet with one usable side (one page).\r
\r
New sheets are added at the end of the binder.\r
\r
### Change the layout\r
\r
Use **Layout** to set how many pockets each page has. The choice applies to the whole binder.\r
\r
| Layout | Pockets per page |\r
|---|---|\r
| 2 × 2 | 4 |\r
| 3 × 3 | 9 |\r
| 3 × 4 | 12 |\r
| 4 × 4 | 16 |\r
\r
Most trading-card binder pages are 3 × 3.\r
\r
### Remove a page\r
\r
In edit mode, the toolbar shows a **Remove page** button for each page on the current spread (for example **Remove page 4** and **Remove page 5**).\r
\r
Removing a page removes the whole physical sheet it's on. For a double-sided sheet, that's both sides. Cards on the removed sheet go back to the Unplaced pool, and the pages after it move up to close the gap. A binder must keep at least one page.\r
\r
> [!WARNING]\r
> **Remove page** works immediately, without asking you to confirm. Check which sheet you're on before you click it.\r
\r
## Troubleshooting\r
\r
- **There's no Edit button.** The binder is in a site you can only view. Ask an administrator for write access.\r
- **Editing shows an error.** You may not have permission to edit binders. Ask an administrator.\r
- **A card I added isn't showing in a pocket.** It's in the Unplaced pool. Click **Edit** and look in the **Unplaced cards** panel.\r
- **A card I'm looking for isn't in the Unplaced panel.** Clear the panel's search box. If it still isn't there, it may be in another location; use **Add card to this pocket…** and look under **In your collection**.\r
`,We=`# Collection

The Collection page lists every single card you own across all your storage locations. Use it to search, sort, edit, move, list for sale, export, and check a decklist against what you have.

## What the Collection page shows

Open [Collection](/collection) from the navigation menu. The page has three parts:

- A **search box** at the top. Type a search and press **Enter** to filter the list.
- The **view button** beside the search box, for switching between saved layouts of the page. See [Saved views](help:saved-views).
- A toolbar with the **Stack duplicates** switch and the **Columns** and **Select** buttons.
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

Click **Columns** to hide columns or change their order. See [Choose columns](help:saved-views#choose-columns).

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

To keep a sort, search and column layout for next time, save it as a view. See [Saved views](help:saved-views).

## Stack duplicates

The **Stack duplicates** switch is on by default. A [saved view](help:saved-views) remembers the setting. With **Default view**, OmniCard remembers your last choice.

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
`,je=`# Dashboard\r
\r
The Dashboard shows what your whole collection cost, what it's worth today, and how much profit your sales have made. It opens when you first sign in.\r
\r
## Open the Dashboard\r
\r
Click **Dashboard** at the top of the sidebar, or go to [the Dashboard](/). If you don't see **Dashboard** in the sidebar, ask an administrator for access.\r
\r
![The Dashboard with the five totals and the By Game and By Category tables](dashboard-overview.png)\r
\r
## The totals\r
\r
The row of tiles at the top sums up everything you own.\r
\r
| Tile | What it shows |\r
|---|---|\r
| **Total Units** | How many items you own: every single card plus every sealed item, counting each copy |\r
| **Cost** | What you paid, based on the purchase price recorded for each card or item |\r
| **Market** | What everything is worth at today's market prices |\r
| **Unrealized** | **Market** minus **Cost**: the gain or loss you'd have if you sold everything today |\r
| **Realized Profit** | Profit from items you've actually sold: what buyers paid, minus what those items cost you, minus marketplace fees |\r
\r
**Unrealized** and **Realized Profit** are green when they're positive and red when they're negative.\r
\r
## The breakdown tables\r
\r
Below the totals, two tables split the same numbers into groups. Each row shows **Units**, **Cost** and **Market** for its **Group**.\r
\r
- **By Game** has one row per game you own cards or product for.\r
- **By Category** splits your holdings by kind of product: single cards, and sealed product such as cases, boxes, packs, decks and bundles.\r
\r
## How the numbers are worked out\r
\r
- **Everything counts.** The Dashboard covers all games, all sites and all locations. Choosing a game in the top bar doesn't change it.\r
- **Traded-away cards are left out** once a trade is finalized. See [Trades](help:trades).\r
- **Single cards** are valued at their current market price times their quantity. Foil and non-foil copies use their own prices.\r
- **Sealed product** is valued at the **Market** price shown for it on the Inventory page. Product with no market price counts as zero. See [Inventory](help:inventory).\r
- **Missing purchase prices count as zero.** If you didn't enter what you paid, **Cost** is understated and **Unrealized** looks bigger than it really is. You can add purchase prices when you scan, or edit them later in the [Collection](help:collection).\r
- **Missing market prices count as zero.** Some cards have no price from their game's price source, so **Market** may be a little low. See [Troubleshooting](help:troubleshooting).\r
- **Realized Profit** is based on the cards and items you've sold. When only part of a stack was sold, only the cost of the sold copies is counted. Marketplace fees come from shipped and completed orders. See [Sales](help:sales).\r
\r
> [!TIP]\r
> Market prices are only as fresh as the last price update. An administrator can refresh them under **Administration ▸ Catalog Data** with **Update prices**. See [Administration](help:administration).\r
\r
## Related topics\r
\r
- [Collection](help:collection): edit purchase prices and see each card's market price\r
- [Locations](help:locations): the value of each binder, box or deck\r
- [Sales](help:sales): orders and listings that feed **Realized Profit**\r
- [Inventory](help:inventory): sealed product and its market prices\r
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
2. Click the **Stacked view** button (the columns icon) to the right of the search box. Click the **Table view** button (the list icon) to switch back. A [saved view](help:saved-views) remembers the choice and the **Group by** setting below.

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
`,Ye=`# eBay\r
\r
Connect OmniCard to your eBay seller account and publish cards from your collection as live eBay listings, without retyping anything on eBay.\r
\r
## What the eBay integration does\r
\r
With eBay connected, you can:\r
\r
- Publish a card from your collection to eBay from the **List for sale** dialog, with a suggested title, description, condition and category.\r
- See which listings are live on eBay, open them, and update their price and details from the **Listings** tab on the [Sales](help:sales) page.\r
- End eBay listings automatically when you unlist a card or ship an order that includes it.\r
\r
OmniCard connects to **one eBay seller account** for your whole OmniCard site. Everyone who lists on eBay publishes under that account.\r
\r
Before you can list on eBay, an administrator sets things up once:\r
\r
1. [Connect your eBay account](help:ebay#connect-your-ebay-account).\r
2. [Enter your seller settings](help:ebay#enter-your-seller-settings).\r
3. [Run seller setup](help:ebay#run-seller-setup).\r
\r
> [!NOTE]\r
> The eBay options are on the **Administration** page, **eBay** tab. Some messages call this **Settings ▸ eBay**. If you can't see the tab, or its buttons and fields are greyed out, ask an administrator for access.\r
\r
## Connect your eBay account\r
\r
1. Go to **Administration ▸ eBay**.\r
2. Check the **Status**:\r
   - **Connected**: you're all set.\r
   - **Not connected**: continue below.\r
   - **Not configured**: the OmniCard server isn't set up for eBay yet. The message lists what's missing. Ask whoever installed OmniCard to add the server's eBay app credentials, then reload the page.\r
3. Click **Connect to eBay**. You're taken to eBay.\r
4. Sign in to your eBay seller account and approve access for OmniCard.\r
5. eBay sends you back to OmniCard, which shows **Connected to eBay.**\r
\r
![The eBay tab with the connection status and seller settings](ebay-settings.png)\r
\r
If you see **eBay connection failed. Please try again.**, click **Connect to eBay** again and finish signing in on eBay.\r
\r
To stop using eBay, click **Disconnect**. Your existing listings in OmniCard stay as they are.\r
\r
## Enter your seller settings\r
\r
eBay needs an address for where your items ship from, plus shipping and return policies. Enter them under **Seller settings** on the same tab.\r
\r
### Inventory location address\r
\r
- **Location name**: a label for this address, for example your store name.\r
- **Address line 1**, **City**, **Postal code** and **Country (ISO-2, e.g. US)** are required. Use a two-letter country code, such as \`US\`.\r
- **Address line 2**, **State / province** and **Phone** are optional.\r
\r
Until the address is complete, a warning reminds you that eBay needs it.\r
\r
### Shipping policy\r
\r
- **Free shipping**: turn this on to offer free shipping, or off to charge a **Flat shipping cost**.\r
- **Handling time (days)**: how soon you ship after a sale.\r
- **Shipping service**: USPS Priority, USPS Ground Advantage, USPS First Class or USPS Media Mail.\r
\r
### Return policy\r
\r
- **Accept returns**: turn this on to accept returns.\r
- **Return window (days)**: how long buyers have to return an item.\r
- **Return shipping paid by**: **Buyer** or **Seller**.\r
\r
When you're done, click **Save seller settings**. Then run seller setup, as described next.\r
\r
## Run seller setup\r
\r
Seller setup creates your shipping location and your shipping, payment and return policies on eBay, using the seller settings you saved.\r
\r
1. Make sure the status is **Connected** and your seller settings are saved.\r
2. Click **Run seller setup**.\r
3. Wait for **Seller setup complete.** If you see **Seller setup failed.**, read the details after the message, fix the problem (often an incomplete address), and run it again.\r
\r
The **Setup status** section shows the result:\r
\r
| Item | What it tells you |\r
|---|---|\r
| **Inventory location created** | Whether eBay has your shipping location |\r
| **Business policies created** | How many of the three policies (shipping, payment, returns) exist, for example 3/3 |\r
| **Setup last completed** | When setup last finished successfully |\r
\r
> [!TIP]\r
> Changed your address or shipping options? Click **Save seller settings**, then **Run seller setup** again so eBay gets the new details. It's safe to run more than once.\r
\r
## List a card on eBay\r
\r
1. Open a card's details, for example by clicking it on the [Collection](help:collection) page or in a location, and click **List for sale**. In a [binder](help:binders), you can also open a card's menu and choose **List for sale**.\r
2. If you own more than one copy, set the **Quantity**. Listing fewer than all of them splits those copies off into their own stack.\r
3. Set the **Price**. It starts at the card's market price.\r
4. Choose **eBay** as the **Channel**. An **eBay listing** section appears, already filled in from the card:\r
   - **Listing title**: built from the card's name, set, number, foil, language and condition. Up to 80 characters, and a counter shows how many you've used.\r
   - **Description**: a short summary of the card. Edit it freely.\r
   - **Condition**: NM, LP, MP, HP or DMG. It starts at the card's condition.\r
   - **Listing type**: **Fixed price** or **Auction**. For an auction, also choose the **Auction duration** (1, 3, 5, 7 or 10 days).\r
   - **eBay category**: the category for individual collectible cards is selected. **Default (trading card singles)** works too.\r
5. Optionally add a **Note**. It stays in OmniCard and isn't sent to eBay.\r
6. Click **List on eBay**.\r
\r
![The List for sale dialog with the eBay listing section](ebay-list-for-sale.png)\r
\r
When it works, the dialog closes. The card shows on the **Listings** tab with the **eBay** channel, plus a **View on eBay** button that opens the live listing. OmniCard uses the card's picture from the card catalog as the listing photo.\r
\r
> [!NOTE]\r
> If eBay isn't connected, the dialog shows "Not connected to eBay. Connect and run seller setup in Settings ▸ eBay before listing." and **List on eBay** stays disabled.\r
\r
### Listing another copy of a card that's already on eBay\r
\r
eBay doesn't allow two identical listings from the same seller. If you list a copy of a card that already has a live eBay listing in the same condition, OmniCard adds it to that listing and raises the listing's quantity. It doesn't create a second listing. The existing listing keeps its current price.\r
\r
## If the eBay listing fails\r
\r
OmniCard always lists the card in OmniCard first. If eBay then rejects the listing, the dialog stays open with **Listed locally, but the eBay push failed:** followed by eBay's reason. Click **Close**.\r
\r
The card now shows on the **Listings** tab with the **eBay** channel, but it isn't live on eBay. There's no **View on eBay** button for it. To try again:\r
\r
1. Fix the cause. Common causes are an incomplete seller setup, a disconnected account, or a title eBay doesn't accept.\r
2. On the **Sales ▸ Listings** tab, click **Unlist** on the card.\r
3. List the card again with the **eBay** channel.\r
\r
## Update a live eBay listing\r
\r
1. On the **Sales ▸ Listings** tab, find the card and click **Update on eBay**.\r
2. In **Update eBay listing**, change the **Price**, **Listing title**, **Description**, **Condition**, **Listing type** or **eBay category**.\r
3. Click **Update on eBay**.\r
\r
The change is published to eBay, and the price in OmniCard updates to match. If eBay rejects the change, you see **eBay update failed:** with the reason, and the live listing stays as it was.\r
\r
## End an eBay listing\r
\r
- **Unlist a card**: on the **Listings** tab, click **Unlist**. If it was the last copy on that eBay listing, the listing is ended on eBay. If other copies of the same card are still listed, the eBay listing's quantity goes down instead.\r
- **Sell a card through an order**: when an order containing the card moves to a shipped lane, OmniCard ends the card's eBay listing so it can't sell twice. See [Sales](help:sales#move-an-order-through-the-board).\r
\r
> [!WARNING]\r
> Ending a listing on eBay can occasionally fail, for example if eBay is unavailable. After shipping an order, check that the item no longer shows as live on eBay, and end it there yourself if it does.\r
\r
## What you can and can't list on eBay\r
\r
- **Singles only.** You can publish individual cards from your collection. Sealed product on the [Inventory](help:inventory) page can't be listed on eBay from OmniCard.\r
- **One card at a time.** Publishing happens only from the **List for sale** dialog for a single card. Listing many cards at once with the **eBay** channel, or switching an existing listing's channel to **eBay** with **Edit**, only marks them for eBay in OmniCard. Nothing is published on eBay.\r
- **eBay orders aren't imported automatically.** When something sells on eBay, create the order on the [Sales](help:sales#create-an-order) page with the **eBay** channel and add the sold card. Moving that order to a shipped lane removes the card from your collection.\r
\r
## Troubleshooting\r
\r
- **The status says "Not configured".** The OmniCard server doesn't have eBay app credentials yet. This is set up on the server by whoever installed OmniCard, not on this page.\r
- **After clicking Connect to eBay, you see "eBay isn't configured on the server yet".** Same cause: the server's eBay setup is incomplete. Ask whoever installed OmniCard to finish it.\r
- **"Seller setup failed."** Check that the address has **Address line 1**, **City**, **Postal code** and a two-letter **Country**, click **Save seller settings**, then run setup again.\r
- **The eBay listing failed.** See [If the eBay listing fails](help:ebay#if-the-ebay-listing-fails).\r
- **There's no "Update on eBay" button.** The card isn't live on eBay. It may have failed to publish, or been listed in bulk.\r
\r
For more help, see [Troubleshooting](help:troubleshooting).\r
`,He=`# Getting started\r
\r
OmniCard helps you scan, organize, value and sell your trading card collection from any browser. This topic covers signing in, finding your way around the screen, the key ideas behind OmniCard, and a walkthrough for your first 30 minutes.\r
\r
## What OmniCard does\r
\r
OmniCard keeps track of every card you own and where it physically lives. You can:\r
\r
- **Scan** cards from photos, a phone camera, a webcam, or a document scanner. OmniCard recognizes each card and its printing for you.\r
- **Organize** cards into locations such as binders, boxes, deck boxes and display cases, grouped under sites (for example, your home and your shop).\r
- **Browse and search** your whole collection, with live market prices.\r
- **Check sets and decklists** to see what you own and what you're missing.\r
- **Track sealed product** (booster boxes, packs, decks) on the Inventory page.\r
- **Trade and sell** cards, including order tracking, receipts and eBay listings.\r
\r
OmniCard supports these games:\r
\r
| Game | Notes |\r
|---|---|\r
| Magic: The Gathering | Also downloads non-English printings if an administrator turns them on |\r
| One Piece TCG | English, Japanese and French printings available |\r
| Pokémon | English, plus Japanese sets with their own prices |\r
| Yu-Gi-Oh! | English catalog |\r
| Final Fantasy TCG | English catalog |\r
| Riftbound | English catalog |\r
\r
## Sign in\r
\r
OmniCard opens on a sign-in screen.\r
\r
1. Enter your **Username** and **Password**.\r
2. Tick **Remember me** if you're on your own device and want to stay signed in for up to 30 days. Leave it unticked on a shared computer. You'll then be signed out when you close the browser.\r
3. Click **Sign in**.\r
\r
![The OmniCard sign-in screen](getting-started-sign-in.png)\r
\r
If you see *Incorrect username or password*, check your typing and try again. An administrator creates accounts and can reset your password. See [Sign-in problems](help:troubleshooting).\r
\r
> [!WARNING]\r
> A brand-new OmniCard server comes with a built-in **Admin** account whose password is \`admin\`. If you're setting OmniCard up, sign in with it and change that password right away under **Administration ▸ Users**.\r
\r
## Find your way around\r
\r
![The main OmniCard screen with the sidebar, top bar and Collection page](getting-started-layout.png)\r
\r
### Top bar\r
\r
- **OmniCard** on the left is the app name.\r
- The **game selector** filters most pages to one game. Choose **All Games** to see everything at once. Your choice is remembered in this browser.\r
- The **Account** button (the person icon) opens your account menu. It shows **Signed in as** with your username, and has **Sign out**. Administrators also see **Account & users**, a shortcut to user management.\r
\r
### Sidebar\r
\r
The sidebar on the left lists the sections you have access to. You might not see all of them. Which ones appear depends on the permissions an administrator gave you.\r
\r
| Section | What it's for | Learn more |\r
|---|---|---|\r
| **Dashboard** | Totals for your collection's cost, market value and profit | [Dashboard](help:dashboard) |\r
| **Scan** | Photograph or upload cards and add them to a location | [Scanning cards](help:scanning) |\r
| **Collection** | Search, edit, move, export and sell cards you own | [Collection](help:collection) |\r
| **Locations** | Binders, boxes, deck boxes and display cases | [Locations](help:locations) |\r
| **Sets** | Set checklists showing what you own | [Sets](help:sets) |\r
| **Inventory** | Sealed product such as boxes, packs and decks | [Inventory](help:inventory) |\r
| **Lists** | Want lists and decklists checked against your collection | [Lists](help:lists) |\r
| **Trades** | Build and record trades | [Trades](help:trades) |\r
| **Import** | Bring in cards from CSV files and deck sites, or export them | [Importing](help:importing) |\r
| **Sales** | Orders, customers and listings | [Sales](help:sales) |\r
| **Administration** | Your password, plus settings and user management | [Administration](help:administration) |\r
| **Help** | This help center, at the bottom of the sidebar | |\r
\r
A number on the **Scan** icon counts scan batches that are waiting for someone to review them. See [Scan batches](help:scan-batches).\r
\r
### On a phone\r
\r
On a narrow screen the sidebar is hidden to save space.\r
\r
1. Tap the **menu button** (three lines) at the top left to open the sidebar.\r
2. Tap a section. The sidebar closes again on its own.\r
\r
The game selector and the account menu stay in the top bar. Most pages work well on a phone. For scanning on a phone, see [Scanning cards](help:scanning).\r
\r
![OmniCard on a phone with the sidebar opened from the menu button](getting-started-phone-menu.png)\r
\r
\r
### Using Help\r
\r
Click **Help** at the bottom of the sidebar at any time. The help home groups every topic by area; type in **Search help** to find topics that mention a word, such as \`binder\` or \`condition\`. Inside a topic, the list on the left jumps between topics, **On this page** (on wide screens) jumps between sections, and **Previous** / **Next** at the bottom walk through the guides in order.\r
\r
![The Help home page with topic search](help-home.png)\r
\r
## Key concepts\r
\r
### Sites, locations and cards\r
\r
OmniCard organizes your collection in three levels: **Site ▸ Location ▸ Card**.\r
\r
- A **site** is a major physical place, such as a home, a shop or a storage unit. Every OmniCard has a **Default** site that everyone can see. Administrators can add more sites, and some people may only see some of them.\r
- A **location** is where cards physically live inside a site. Location types are **Binder**, **Box**, **Deck Box** and **Display Case**. There's also **Bulk** for cards that aren't sorted anywhere special.\r
- A **card** is a copy you own, kept in one location.\r
\r
See [Locations](help:locations), [Binders](help:binders) and [Deck boxes](help:deck-boxes).\r
\r
### Stacks and quantity\r
\r
Identical copies in the same location can be kept together as one entry with a **Quantity** greater than 1. This is called a stack. You can split a stack later, for example so each copy gets its own binder pocket. See [Collection](help:collection).\r
\r
### Card details\r
\r
Each card you own records:\r
\r
- **Condition** using the usual grades: **NM** (near mint), **LP** (lightly played), **MP** (moderately played), **HP** (heavily played) and **DMG** (damaged).\r
- **Foil** for foil copies. Some games also record the foil type.\r
- **Language**, which is English unless you choose another. OmniCard can read the printed language on some cards when you scan them.\r
- **Purchase price**, used to work out your cost and profit.\r
- **Tags** and a **Note** for anything else, for example "signed" or "misprint".\r
\r
### Prices\r
\r
Market prices come from each game's price source and are refreshed by an administrator. Some cards have no market price. See [Missing prices or images](help:troubleshooting).\r
\r
### Permissions\r
\r
What you can see and do depends on your account:\r
\r
- **Administrators** can see and do everything.\r
- Everyone else gets a **role**, such as *Viewer* (look but don't change) or *Staff* (everyday work, without deleting or changing settings). An administrator can also give or remove individual permissions.\r
- **Site access** decides which sites you can see, and whether you can only view them or also change them.\r
\r
If a section, button or site is missing, ask an administrator for access. See [Administration](help:administration).\r
\r
## Your first 30 minutes\r
\r
This walkthrough takes you from a fresh setup to a searchable collection. Steps 1 and 2 need an administrator. If someone has already set OmniCard up, skip ahead to step 3.\r
\r
### Step 1: Download catalog data (administrator)\r
\r
OmniCard recognizes cards by comparing them with each game's card catalog. The catalog needs to be downloaded before you scan.\r
\r
1. Go to **Administration ▸ Catalog Data**.\r
2. Pick a **Game**.\r
3. Click **Download catalog** and wait for it to finish. Progress shows below the buttons.\r
4. Click **Update prices** to get market prices.\r
5. Optionally, click **Download artwork** so card images load from your own server.\r
6. Repeat for each game you collect.\r
\r
Only one job runs at a time. A big catalog such as Magic: The Gathering can take a while. See [Administration](help:administration).\r
\r
### Step 2: Add users (administrator)\r
\r
If other people will use OmniCard, add an account for each of them under **Administration ▸ Users**, and give each one a role. If you have more than one physical place, set up **Sites** too. See [Administration](help:administration).\r
\r
### Step 3: Create your locations\r
\r
1. Open [Locations](/locations).\r
2. Type a **New location name**, for example "Red binder".\r
3. Choose a **Type**: **Binder**, **Box**, **Deck Box** or **Display Case**. A deck box also needs a game.\r
4. Click **Add**.\r
\r
Create a location for each binder, box or deck you want to track. See [Locations](help:locations).\r
\r
### Step 4: Get cards into OmniCard\r
\r
Pick whichever way suits you:\r
\r
- **Scan them.** Open [Scan](/scan), choose the **Game**, then click **Take photo**, **Use webcam** or **Add images**. Review the matches and fix any that are wrong with **Search catalog**. Click **Add to location…** to pick where they go, click **Confirm checked** to accept the matches, then click **Add confirmed cards**. See [Scanning cards](help:scanning).\r
- **Import them.** If you already track your cards in TCGplayer, Moxfield or ManaBox, export a CSV file there and bring it in on [Import](/import). You can also import a public Moxfield or Archidekt deck straight from its link. See [Importing](help:importing).\r
- **Add them one at a time.** Open a location and use **Add card**. See [Locations](help:locations).\r
\r
> [!TIP]\r
> If you have a document scanner, an administrator can set up a watched folder. Scans saved there are matched in the background and wait for you on the Scan page. See [Scan batches](help:scan-batches).\r
\r
### Step 5: Browse your collection\r
\r
1. Open [Collection](/collection).\r
2. Type in the search box to find cards. Plain text searches by name. You can also use search terms such as \`t:creature\`. See [Search syntax](help:search-syntax).\r
3. Click a card to change its condition, language, quantity, price, location or tags.\r
4. Check the [Dashboard](/) to see what your collection is worth.\r
\r
## Where to go next\r
\r
- [Scanning cards](help:scanning): every scanning option, corrections and badges\r
- [Scan batches](help:scan-batches): reviewing scans from a watched scanner folder\r
- [Auditing a location](help:location-audit): rescan a binder or box to check it's accurate\r
- [Collection](help:collection) and [Search syntax](help:search-syntax): finding and editing cards\r
- [Sets](help:sets): set checklists and want lists\r
- [Locations](help:locations), [Binders](help:binders) and [Deck boxes](help:deck-boxes): organizing your cards\r
- [Lists](help:lists): decklists and want lists checked against what you own\r
- [Importing](help:importing): CSV files and deck links\r
- [Trades](help:trades): recording trades\r
- [Inventory](help:inventory): sealed product\r
- [Sales](help:sales) and [eBay](help:ebay): selling cards\r
- [Dashboard](help:dashboard): collection value and profit\r
- [Administration](help:administration): settings, users, sites and catalog data\r
- [Troubleshooting](help:troubleshooting): answers to common problems\r
`,Ve=`# Importing and exporting\r
\r
Bring cards into OmniCard from a CSV file or a Moxfield or Archidekt deck, and export your collection to CSV for other apps. Everything is on the Import / Export page.\r
\r
## What this page is for\r
\r
The **Import / Export** page has three parts:\r
\r
- **Import collection (CSV)**: add cards from a collection spreadsheet that OmniCard or another app exported.\r
- **Import from Moxfield / Archidekt**: add every card in a public deck to a location, as cards you own.\r
- **Export collection (CSV)**: download your collection in a format another app can read.\r
\r
Open it from **Import** in the navigation menu, or go to [Import](/import).\r
\r
![The Import / Export page with the CSV import, deck URL import, and export sections](importing-page.png)\r
\r
> [!NOTE]\r
> If you don't see **Import** in the menu, ask an administrator for access. You can import only into locations at sites you're allowed to change.\r
\r
## Import a CSV file\r
\r
1. Open [Import](/import).\r
2. Under **Import collection (CSV)**, click **Choose CSV file** and pick your file. The button changes to show the file's name.\r
3. Leave **Skip duplicates already in collection** ticked to avoid adding cards you already have. See **Duplicates** below.\r
4. Optionally, choose a **Target location (optional)** for the cards. If you leave it on **— none —**, the cards are added without a location.\r
5. Click **Import**.\r
\r
When the import finishes, a message shows how many rows were imported, out of how many, and which format OmniCard detected. Rows that couldn't be read are skipped and counted as warnings.\r
\r
### Supported CSV formats\r
\r
OmniCard reads the file's column headings to tell the format apart. You don't need to choose a format.\r
\r
| Format | Where it comes from | Games |\r
|---|---|---|\r
| App-native | OmniCard's own export | All games |\r
| TCGplayer | TCGplayer collection export | Magic: The Gathering |\r
| Moxfield | Moxfield collection export | Magic: The Gathering |\r
| ManaBox | ManaBox collection export | Magic: The Gathering |\r
\r
If OmniCard doesn't recognize the file, nothing is imported. Export the file again from the other app, and don't edit its column headings.\r
\r
### How rows are read\r
\r
- **Quantity**: a blank quantity counts as 1.\r
- **Condition**: codes like NM, LP, MP, HP, and DMG work, and so do full names like *Near Mint* or *Lightly Played*, and ManaBox spellings like *near_mint*. A blank or unrecognized condition is imported as NM.\r
- **Foil**: foil cards get the game's standard foil finish unless the file names a finish.\r
- **Language**: a *Language* column is read if the file has one. Codes like \`ja\` and names like *Japanese* both work. A blank or unrecognized language is imported as English.\r
- **Purchase price**: kept when the file includes it.\r
\r
### Duplicates\r
\r
With **Skip duplicates already in collection** ticked, OmniCard skips a row when you already own a copy of the same printing, with the same finish, condition, and language, anywhere in your collection. It skips the whole row, not just the extra copies. For example, if you own one copy of a card and the file lists four, none of the four are added.\r
\r
Untick the option to add every row as new cards, even when you already own matching copies. That's useful when a file lists new copies of cards you already have.\r
\r
> [!WARNING]\r
> If you import the same file twice with **Skip duplicates already in collection** unticked, every card is added twice.\r
\r
## Import a deck from Moxfield or Archidekt\r
\r
This adds every card in a public Moxfield or Archidekt deck to one location, as cards you own. Use it when you've built a deck in real life and want it in your collection. These sites are for Magic: The Gathering only.\r
\r
1. Under **Import from Moxfield / Archidekt**, paste the deck's address into **Deck URL**.\r
2. Choose a **Target location**. This is required.\r
3. Choose the **Condition** for all the cards (NM by default).\r
4. Optionally, tick **Skip duplicates already in collection**. It's unticked by default for deck imports.\r
5. Click **Import**.\r
\r
OmniCard keeps each card's exact printing and its foil or etched finish from the deck. When a deck lists the same card more than once, the copies are combined into one stack.\r
\r
The result message shows:\r
\r
- How many cards were imported, out of how many in the deck.\r
- How many were skipped as duplicates, if you ticked that option.\r
- Cards whose exact printing wasn't in the catalog. OmniCard used the cheapest printing of the same card instead, so check these later.\r
- Cards that couldn't be found at all. Add these by hand.\r
\r
![A finished deck URL import showing substituted and unmatched cards](importing-url-result.png)\r
\r
> [!TIP]\r
> If you want to track a deck without adding the cards to your collection, use [Lists](help:lists) instead. Lists show what you own and what you need to buy.\r
\r
## Export your collection\r
\r
1. Choose a game with the game selector at the top of the app, or choose **All Games** to export everything.\r
2. Under **Export collection (CSV)**, click a format. The file downloads right away.\r
\r
| Format | Use it for |\r
|---|---|\r
| **App-native** | A full OmniCard backup, or moving cards between OmniCard installs |\r
| **TCGplayer** | TCGplayer |\r
| **Moxfield** | Moxfield (Magic only) |\r
| **Manabox** | ManaBox (Magic only) |\r
| **Archidekt** | Archidekt (Magic only) |\r
| **Deckbox** | Deckbox (Magic only) |\r
| **Dragon Shield** | Dragon Shield card manager |\r
| **Text list** | A plain list, one line per card, that you can paste into most deck builders |\r
\r
## Other ways to import\r
\r
- **Into one location, all or nothing**: open a location and click **Import**. You can use a CSV file or a deck URL. Every card goes into that location, and if any line has a problem, nothing is imported and each problem is listed so you can fix it. See [Locations](help:locations).\r
- **Checking a decklist**: to see which cards in a decklist you own and which are missing, without importing anything, use **Check decklist** on the Collection page. See [Collection](help:collection).\r
- **Saving a deck to build later**: import its URL on the Lists page. See [Lists](help:lists).\r
- **Sales orders**: order CSV files, such as TCGplayer order exports, are imported from **Sales ▸ Orders** with **Import CSV**. See [Sales](help:sales).\r
- **Scanning cards**: to add physical cards with a scanner or camera, see [Scanning](help:scanning).\r
\r
## Troubleshooting\r
\r
- **"No importable rows found."**: the file's column headings don't match a supported format, or no row could be read. Export it again from the original app.\r
- **Fewer cards than expected**: **Skip duplicates already in collection** may have skipped rows you already own. Import again with it unticked, but only for the rows you need, or you'll add copies twice.\r
- **"Couldn't fetch that deck URL"**: make sure the deck is public, and that the address is a Moxfield or Archidekt deck page.\r
- **A card shows the wrong printing after a deck import**: its exact printing probably wasn't in the catalog, so the cheapest printing was used. The result message lists these cards. To fix one, see [Collection](help:collection).\r
\r
For other problems, see [Troubleshooting](help:troubleshooting).\r
`,ze=`# Sealed inventory\r
\r
Track sealed product such as booster boxes, packs, decks and bundles: what you have, what you paid, what it's worth, and where it's stored.\r
\r
## What the Inventory page is for\r
\r
The **Inventory** page (titled **Sealed Inventory**) holds unopened product. Individual cards (singles) don't live here. They're in your [collection](help:collection).\r
\r
Inventory is organized in two levels:\r
\r
- A **product** is the thing itself, such as "Bloomburrow Play Booster Box". It has a game, a type, set details, a UPC and a market price.\r
- A **lot** is a batch of that product you own: how many units, what each one cost, where they're stored and where they came from. One product can have many lots, for example a box bought at release and two more bought later at a different price.\r
\r
Open it from the **Inventory** item in the navigation menu, or go to [Inventory](/inventory).\r
\r
![The Sealed Inventory page with value totals and the product list](inventory-page.png)\r
\r
> [!NOTE]\r
> If you don't see **Inventory** in the menu, ask an administrator for access.\r
\r
## Read the page\r
\r
At the top, three totals summarize all your sealed inventory:\r
\r
| Total | What it shows |\r
|---|---|\r
| **Total Units** | How many sealed items you own, across every lot |\r
| **Cost** | What you paid, based on each lot's unit cost |\r
| **Market** | What the product is worth at its market price |\r
\r
Below the totals, the product list shows one row per product:\r
\r
| Column | Meaning |\r
|---|---|\r
| **Product** | The product name |\r
| **Type** | Case, Box, Pack, Deck, Bundle or Other |\r
| **Set** | The set code |\r
| **Qty** | Total units across all of the product's lots |\r
| **Market** | The market price of one unit |\r
| **UPC** | The product's barcode number, if you entered one |\r
\r
Click a column header to sort by it.\r
\r
### Filter by game\r
\r
The product list follows the game selector at the top of the app. Pick a game to see only that game's products, or choose **All Games** to see everything. The three totals always cover your whole inventory.\r
\r
## Add a new product\r
\r
1. Click **New product**.\r
2. In **New sealed product**, enter a **Name**. It's the only required field.\r
3. Choose the **Game** and the **Type** (Case, Box, Pack, Deck, Bundle or Other). The game starts as the one picked in the game selector.\r
4. Optionally fill in **Set name**, **Set code**, **UPC** and **Market price**.\r
5. Click **Save**.\r
\r
![The New sealed product dialog](inventory-new-product.png)\r
\r
A new product has no units yet. Add a lot to record what you own.\r
\r
> [!TIP]\r
> Enter the UPC from the box's barcode. It shows in the **UPC** column, so you can find a product by the number printed on the package.\r
\r
## Add units you own (lots)\r
\r
1. Click a product in the list. Its details panel opens on the right.\r
2. In the **Lots** section, click **Add lot**.\r
3. Fill in the lot:\r
   - **Quantity**: how many units are in this lot (at least 1).\r
   - **Unit cost**: what you paid for each one.\r
   - **Location (optional)**: where the product is stored. Choose from your [storage locations](help:locations).\r
   - **Source (optional)**: where you got it, such as a store name or "Preorder".\r
4. Click **Save**.\r
\r
![A product's details panel with its lots](inventory-product-drawer.png)\r
\r
The product's **Qty** and the page totals update right away.\r
\r
> [!TIP]\r
> Bought the same product at different prices? Add a separate lot for each purchase. Your **Cost** total stays accurate, and each lot keeps its own source.\r
\r
## Edit, move or remove a lot\r
\r
In the product's details panel, each lot shows its **Qty**, **Cost** and **Source**, with buttons on the right.\r
\r
- **Edit a lot**: click the pencil button, change the fields and click **Save**. Lower the **Quantity** after you open or sell some units.\r
- **Move a lot**: click the pencil button and pick a different **Location (optional)**. Choose **— none —** if it isn't stored anywhere in particular.\r
- **Delete a lot**: click the trash button and confirm. This removes the lot and its units for good.\r
\r
## Edit or delete a product\r
\r
At the top of a product's details panel:\r
\r
- Click the pencil button (**Edit product**) to change the name, game, type, set details, UPC or market price.\r
- Click the trash button (**Delete product**) to delete the product. You're asked to confirm, because **this also deletes all of its lots**.\r
\r
### Keep market prices current\r
\r
The **Market price** of a sealed product is a value you enter. To keep the **Market** total meaningful, open **Edit product** and update the price now and then.\r
\r
## Tips\r
\r
- Use specific product names, including the set and product type (for example "Duskmourn Collector Booster Box"), so similar products are easy to tell apart.\r
- Use lots to tell purchases apart, and products to tell different items apart.\r
- To store sealed product next to your cards, create a location for it on the [Locations](help:locations) page, then choose it on each lot.\r
- Selling cards rather than sealed product? See [Sales](help:sales) and [eBay](help:ebay).\r
`,qe=`# Lists\r
\r
Lists are saved card lists, such as a deck you want to build or cards you plan to buy. OmniCard shows which cards you already own and where they are, then helps you pull, buy, and put them away.\r
\r
## What lists are for\r
\r
A list is a set of cards for one game, each with a quantity. A list never changes your collection by itself. It's a plan. For every card on the list, OmniCard checks your collection and shows how many copies you already own. It also shows what the rest would cost to buy.\r
\r
When the deck comes together, use **Put cards away**. OmniCard moves the copies you own into the deck's location, and it adds the copies you bought as new cards. You don't have to do any of this card by card.\r
\r
Open lists from **Lists** in the navigation menu, or go to [Lists](/lists).\r
\r
![The Lists page with a list selected, showing owned counts and the Put cards away panel](lists-overview.png)\r
\r
> [!NOTE]\r
> If you don't see **Lists** in the menu, or some buttons are missing, ask an administrator for access.\r
\r
## Create a list\r
\r
1. Open [Lists](/lists).\r
2. Pick the **Game** at the top left. Each list belongs to one game, and the page shows only lists for the game you pick.\r
3. Type a name in **New list name**.\r
4. Click **Create**.\r
\r
The new list appears as a chip under the top panel and opens right away. Each chip shows the list's name and how many cards it holds. Click a chip to open that list.\r
\r
### Rename or delete a list\r
\r
- To rename the open list, click **Rename** above it and type the new name.\r
- To delete a list, click the delete icon on its chip and confirm. Deleting a list doesn't change your collection.\r
\r
## Import a list from Moxfield or Archidekt\r
\r
You can turn a public Moxfield or Archidekt deck into a new list.\r
\r
1. Open [Lists](/lists) and pick the **Game**.\r
2. Under **or import from a URL**, paste the deck's address into **Moxfield / Archidekt deck URL**.\r
3. Optionally, choose a card language next to it. Leave it on **Any language** to count copies in any language. See [Set the card language](#set-the-card-language).\r
4. Click **Import as new list**, or press Enter.\r
\r
OmniCard names the list after the deck and opens it. A message tells you how many cards were added. If some cards couldn't be matched to the catalog, they're named in the message so you can add them by hand.\r
\r
The list remembers the deck's address, so you can update it later when the deck changes. See [Update a list from its URL](#update-a-list-from-its-url).\r
\r
### Add a deck to an existing list\r
\r
To add a whole deck to the list that's already open, paste its address into **Add from URL (Moxfield / Archidekt)** in the list, then click **Add**.\r
\r
## Add cards by hand\r
\r
1. Open the list and click **Add card**.\r
2. In the **Add card to list** dialog, check the **Game**, then search by **Name**. You can narrow the search with **Set** and **Collector #**. If the list has a card language, a switch such as **Only Japanese cards** limits both groups of results to that language. Turn it off to see every language.\r
3. Results come in two groups:\r
   - **In your collection**: copies you already own. Each shows its set, number, condition, and location. Click one to add that copy to the list. The copy doesn't move yet. When you put the list away, that copy is the one that gets moved.\r
   - **In the catalog**: every printing in the game's catalog, including ones you don't own. Click one, set the **Qty** and **Foil**, then click **Add to list**.\r
4. The dialog stays open so you can add several cards in a row. The title shows how many you've added so far. Click **Close** when you're done.\r
\r
![The Add card to list dialog with results from your collection and the catalog](lists-add-card-dialog.png)\r
\r
## Read a list\r
\r
The open list splits its cards into two grids:\r
\r
- **Owned**: the copies your collection already covers.\r
- **To buy**: the copies you still need.\r
\r
A card you partly own shows up in both grids. For example, if the list needs 4 and you own 1, it shows 1 under **Owned** and 3 under **To buy**. Each grid's heading shows how many copies it holds. Click a heading to collapse or expand that grid. OmniCard remembers your choice in this browser. The **Export** button on a heading exports just that grid. See [Export a list](#export-a-list).\r
\r
Each grid has these columns:\r
\r
| Column | What it shows |\r
|---|---|\r
| (icon) | Whether you own the card. See the icons below. |\r
| **Card** | The card name. ✦ means foil. Hover over the name to see the card image. |\r
| **Set** | The set code and collector number. |\r
| **Owned** / **To buy** | How many copies of this card are in this grid. |\r
| **List qty** | How many the list needs in total. Type a new number to change it. |\r
| **Price** | The current market price for one copy. A dash means no price is known. |\r
\r
Click the delete icon at the end of a row to take that card off the list.\r
\r
### Icons in the first column\r
\r
- **Collection icon**: you own this printing. It's green when you own enough copies and orange when you own some but not all.\r
- **Swap icon**: a stand-in, meaning another printing from your collection is filling in for the card. See [Use other printings you own](#use-other-printings-you-own).\r
- **Shopping cart**: awaiting purchase. Your copies were already moved and the rest still need to be bought. See [Awaiting purchase](#put-cards-away).\r
\r
In the **To buy** grid, a grey **+N** next to the count means you have N more copies, but they're in an ignored location or listed for sale, so the list doesn't count them. See [Ignored locations](#ignored-locations).\r
\r
### What counts as owned\r
\r
A copy counts as owned only when it's the same printing and the same finish (foil or not), in a site you can see. A copy of the same printing in another language also counts, unless the list is set to one language. Copies that are listed for sale, sitting in an ignored location, flagged missing, or traded away don't count.\r
\r
### Value totals\r
\r
Above the table, **Total market value** is the cost of every card on the list. **To buy** is the cost of only the copies you don't own. Click **Refresh prices** to get the latest market prices.\r
\r
## Update a list from its URL\r
\r
When a Moxfield or Archidekt deck changes, you can bring those changes into your list. Nothing changes until you approve it.\r
\r
1. Open the list and click **Update from URL**. Hover over the button to see the address the list came from.\r
2. If the list remembers its deck address, OmniCard checks it right away. Otherwise, paste an address into **Moxfield / Archidekt deck URL** and click **Check for changes**.\r
3. Review the changes. The summary shows how many cards changed and how many didn't. Each change is marked:\r
   - **Added**: the card is new in the deck. If you already own copies, a note says *you own N*.\r
   - **Removed**: the card is no longer in the deck.\r
   - **Quantity**: the deck has a different count, shown as old → new.\r
4. Tick the changes you want. All changes start ticked except removals of cards marked **(not from the URL)**. Those are cards you added by hand or stand-ins, so OmniCard leaves them unticked to keep them.\r
5. Click **Apply N changes**.\r
\r
If the list already matches the deck, you'll see **The list already matches the deck.**\r
\r
![The Update from URL dialog listing added, removed, and quantity changes](lists-update-from-url.png)\r
\r
## Use other printings you own\r
\r
Sometimes you don't own the exact printing on the list, but you do own the same card from another set. **Find in collection** lets those copies stand in.\r
\r
1. Open the list and click **Find in collection**.\r
2. For each card you're short on, the dialog shows how many you **need**. Below that are the other printings you own, with their condition, location (including page and slot for binders), and how many are **Available**.\r
3. Each **Use** box starts at a suggested amount. Change the amounts as you like. You can't use more copies than a card needs, or use the same copies for two cards. If you try, OmniCard highlights the problem.\r
4. Click **Use N copies**.\r
\r
The copies you chose become stand-ins on the list and get the swap icon. When you put the list away, those exact copies are moved.\r
\r
Greyed-out copies are in ignored locations or listed for sale, so you can't use them. The tag on each one says why: **Ignored location** or **Listed for sale**. To change which locations are ignored, click **Ignored locations…** at the bottom of the dialog.\r
\r
If the list has a card language set, only copies in that language are offered.\r
\r
![The Find in collection dialog offering other printings as stand-ins](lists-find-in-collection.png)\r
\r
## Set the card language\r
\r
Use **Card language** at the top right of an open list to make the list language-specific, for example a Japanese-only deck.\r
\r
- **Any language** (the default): copies in any language count as owned. Imported and new cards use the English printing.\r
- A specific language: only copies in that language count as owned. Imported cards, the buy list, and new cards all use that language's printing.\r
\r
You can also choose the language when you import a list from a URL.\r
\r
When you change the language, OmniCard switches every card on the list to that language's printing of the same set and number. A card that has no printing in that language keeps its English printing and gets a tag such as **No Japanese printing**. Hover over the tag for details. Cards you added from your collection, and stand-ins, keep the exact copy you chose.\r
\r
> [!TIP]\r
> If a list imported before this worked shows printings in the wrong language, click **Refresh prices**. It also switches the cards to the list's language.\r
\r
## Ignored locations\r
\r
Some cards shouldn't be pulled into a list, like your sales binder or a deck you're playing. Ignore their locations, and lists won't count those cards or take them.\r
\r
1. In an open list, click **Ignored locations…** in the **Put cards away** panel. You'll also find it in **Find in collection** and on the [Locations](/locations) page.\r
2. Tick each location to ignore. Locations are grouped by type, and by site if you have more than one site. Tick a group heading to ignore the whole group, or a site heading (marked *whole site*) to ignore everything at that site.\r
3. Use **Filter locations** to find a location by name.\r
4. Click **Save N changes**.\r
\r
Locations in a site you can only read have a lock icon and can't be changed. Ignored locations also apply to decklist checks. For more about locations, see [Locations](help:locations).\r
\r
## Print a list\r
\r
Click **Print** in an open list and choose:\r
\r
- **Print list**: every card on the list.\r
- **Print pick list**: the copies you own, grouped by where they are, so you can walk from location to location and pull them.\r
- **Print buy list**: only the copies you still need to buy.\r
\r
Each one downloads as a PDF.\r
\r
## Export a list\r
\r
Export a list as text to paste into Moxfield or Archidekt, or as a CSV for a spreadsheet.\r
\r
1. Click **Export** above the list. You can also click **Export** on the **Owned** or **To buy** heading to start with that part.\r
2. Choose what to export: **All cards**, **To buy**, or **Owned**.\r
3. Choose **Text** or **CSV**. A preview shows exactly what you'll get.\r
4. Click **Copy** to put it on the clipboard, or **Download** to save it as a file.\r
\r
Text has one line per card, like this:\r
\r
\`1x Aragorn, the Uniter (LTR) 192\`\r
\r
That's the quantity, the card name, the set code in brackets, and the collector number. Foils end in \`*F*\`, and etched foils in \`*E*\`. Copies of the same printing and finish are combined into one line.\r
\r
The CSV has the columns **Qty**, **Card Name**, **Set**, **Collector Number**, and **Foil**.\r
\r
## Put cards away\r
\r
When you've collected the cards, the **Put cards away** panel moves everything into place in one step.\r
\r
1. **Move N owned cards to**: click **Choose location…** and pick where your owned copies go, such as the deck box.\r
2. **Add N new cards to**: click **Choose location…** and pick where your newly bought copies go. Set **Cond** for the new cards (NM by default).\r
3. When you pick a location for one row, the other row uses it too, unless you've already set it. Usually both go to the same place.\r
4. Click one of these:\r
   - **Move owned**: moves only the copies you own.\r
   - **Add new**: adds only the missing copies as new cards in your collection.\r
   - **Move & add**: does both.\r
\r
Here's what happens:\r
\r
- Owned copies move to the chosen location. If a copy is part of a larger stack, OmniCard splits off just the copies it needs.\r
- New cards are added in the list's card language, or in English when the list allows any language.\r
- Cards that are done come off the list. When every card is done, the list is deleted and a message confirms it.\r
\r
In the location picker, you can't choose a deck box that's set to a different game. For more about choosing and creating locations, see [Locations](help:locations) and [Deck boxes](help:deck-boxes).\r
\r
![The Put cards away panel with locations chosen for owned and new cards](lists-put-cards-away.png)\r
\r
### Awaiting purchase\r
\r
If you click **Move owned** before you've bought the rest, the cards you moved come off the list. The cards that still need buying stay on the list with a shopping-cart icon. This means *your copies were already moved, the rest is to buy*. Those cards won't count other copies in your collection as owned, so the same copies aren't counted twice. When you've bought the cards, use **Add new** to add them.\r
\r
## Tips\r
\r
- Lists also work as shopping lists. Import a deck, click **Print buy list**, and take the PDF to a card shop. To order online, export **To buy** as text and paste it into the store's mass-entry box.\r
- Before you put a list away, ignore locations you never want to pull from, like a sales binder.\r
- If a card shows as not owned but you're sure you have it, check its printing, foil, and language. Then try **Find in collection**.\r
- To check a decklist against your collection without saving it, use **Check decklist** on the Collection page. See [Collection](help:collection).\r
- To add a whole deck straight into a location as owned cards, see [Importing](help:importing).\r
- For decks you've already built, see [Deck boxes](help:deck-boxes).\r
`,$e=`# Auditing a location\r
\r
An audit checks a location against what is physically in it. You scan every card that is really there, and OmniCard updates the location to match: cards you scanned are kept, cards you didn't scan are removed, and new cards are added.\r
\r
## When to audit\r
\r
Use an audit when a location's contents in OmniCard may have drifted from reality, for example:\r
\r
- A box or binder has been sorted, traded from, or reorganized without updating OmniCard.\r
- You want to verify a deck box before a trade or sale.\r
- You are cleaning up after an import and want the scan to be the final word.\r
\r
If you only want to add cards to a location, use the normal [Scan](help:scanning) page instead. An audit can delete cards.\r
\r
## Start an audit\r
\r
1. Open [Locations](/locations) and click the location you want to audit.\r
2. On the location's page, click **Audit** (next to **Import** and **Add card**).\r
\r
The audit page opens, titled *Audit: location name*. A banner reminds you that committing makes this scan the source of truth for the location.\r
\r
![A location page with the Audit button next to Import and Add card](location-audit-button.png)\r
\r
> [!NOTE]\r
> The **Audit** button appears only if you are allowed to delete cards and can change the location's site. If you don't see it, ask an administrator for access.\r
\r
## Scan the location\r
\r
The audit page works just like the [Scan](help:scanning) page, except that the target location is locked: instead of **Add to location…** you see the location's name with a lock icon.\r
\r
1. Choose the **Game**, and if you like the **Sets (art fallback)**, **Condition**, **Card language** and **Foil** defaults.\r
2. Add a picture of every card in the location with **Take photo**, **Use webcam** or **Add images**.\r
3. Review each match. **Confirm match** the correct ones and use **Search catalog** to fix the wrong ones. See [Review the list](help:scanning#review-the-list) and [Correct a wrong match](help:scanning#correct-a-wrong-match).\r
4. Set each card's **Condition** and **Foil** to what you see. These overwrite the stored values for matching cards (see below).\r
5. When every card is confirmed and checked, click **Commit audit (N cards)**.\r
\r
![The audit page locked to a location, with the Commit audit button in the action bar](location-audit-page.webp)\r
\r
Only confirmed and checked scans count toward the audit. **Commit audit** stays disabled until every scan has finished matching.\r
\r
> [!WARNING]\r
> Any card in the location that isn't among the confirmed, checked scans is **deleted from your collection** when you commit (it isn't moved anywhere else). Before committing, make sure you scanned everything, that every card you want to keep is confirmed and checked, and that no filter is hiding rows: the commit only counts cards visible in the list. Click **Clear** on the filter bar first if you used one.\r
\r
## What committing does\r
\r
When you commit, OmniCard compares the scan with the location card by card:\r
\r
| Result | What happens |\r
|---|---|\r
| **Matched** | The card was in the location and you scanned it. It stays, and its condition and foil are updated from the scan. |\r
| **Not found** | The card was in the location but you didn't scan it. It is deleted from your collection. |\r
| **Added** | You scanned a card that wasn't recorded in the location. It is added as a new card. |\r
\r
A few details:\r
\r
- Cards are compared by printing. A foil and a non-foil copy of the same printing count as the same card, and the scan decides whether it is foil.\r
- Quantities are compared too. If the location had three copies and you scanned two, one copy is removed. If you scanned four, one is added.\r
- Sealed products stored in the location are not touched by an audit.\r
- A deck box set to one game won't accept cards from another game; the commit is refused if you scanned any.\r
\r
## The audit summary\r
\r
After the commit, you return to the location's page and the **Audit complete** window opens. It starts with a one-line count, for example *12 matched, 2 added, 3 not found (removed)*, and notes how many matched cards had their condition or foil overwritten.\r
\r
Below that are three sections you can expand:\r
\r
- **Matched** — cards that were found.\r
- **Not found — removed** — cards that were removed because you didn't scan them.\r
- **Added** — new cards added from the scan.\r
\r
Each line shows the card name, set and collector number, condition, foil, and quantity. Click **Done** to close the window.\r
\r
![The Audit complete window with the Matched, Not found and Added sections](location-audit-summary.png)\r
\r
## Pausing an audit\r
\r
Your audit scans are saved in this browser as you work, separately for each location and separately from the main Scan page. If you leave the page or refresh it, open **Audit** on the same location again and you'll see *Restored N scans from your last session*. Click **Discard** in that message to start the audit over.\r
\r
As on the Scan page, the saved scans stay in this browser on this device only, and aren't kept in a private or incognito window.\r
\r
## Tips\r
\r
- Audit one location at a time, and keep the cards you have scanned separate from the ones you haven't.\r
- For a large location, sort by **Confidence** to review the weakest matches first.\r
- You can **Export** an audit's scans without committing, for example to keep a record of what was in the box. See [Export scans without adding them](help:scanning#export-scans-without-adding-them).\r
- To find where a card is stored before you audit, use [Collection](help:collection). For more on locations, see [Locations](help:locations).\r
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
- A search box, the view button for [saved views](help:saved-views), and the card list.

### Find cards in a location

Type in the search box to filter the cards. Plain text matches card names, and you can use the full search syntax, such as \`t:creature\` or \`set:mh3\`. See [Search syntax](help:search-syntax).

### Table view and stacked view

Use the two buttons to the right of the search box to switch views. A [saved view](help:saved-views) remembers which one you use. With **Default view**, OmniCard remembers your last choice.

- **Table view** (list icon): a sortable table with name, set, number, rarity, condition, language, foil, quantity, market price and sale status. Turn on **Stack duplicates** to combine identical copies into one row.
- **Stacked view** (columns icon): cards drawn as overlapping stacks grouped by type or tag, like a deck-building site. It works for any location but is most useful for decks. See [Deck boxes](help:deck-boxes#stacked-view).

Each location keeps its own saved views, so a binder and a deck box can open with different layouts. To give several locations the same layout, use **Copy to other locations…**. See [Saved views](help:saved-views#copy-a-view-to-other-locations).

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
`,Je=`# Sales\r
\r
Sell cards from your collection: list them for sale, pull them for shipping, track orders from creation to completion, and keep a customer list.\r
\r
## How selling works in OmniCard\r
\r
Selling usually follows these steps:\r
\r
1. **List** a card for sale from your collection, setting a price and a sales channel (Manual, TCGplayer or eBay).\r
2. **Pick** it: pull the card from where it's stored, and optionally move it to your for-sale location.\r
3. **Record the order** for the buyer and add the cards they bought.\r
4. **Move the order across the board** as you pack, ship and complete it. When it reaches a shipped lane, OmniCard records the sale and removes the sold cards from your collection.\r
\r
The [Sales](/sales) page has three tabs: **Orders**, **Listings** and **Customers**.\r
\r
![The Sales page Orders board](sales-orders-board.png)\r
\r
> [!NOTE]\r
> If you don't see **Sales** in the menu, or some buttons are missing, ask an administrator for access.\r
\r
## List cards for sale\r
\r
You list cards from wherever you view them, not from the Sales page.\r
\r
### List one card\r
\r
1. Open a card's details. For example, click a card on the [Collection](help:collection) page or in a location, or open a card's menu in a [binder](help:binders) and choose **Card details**.\r
2. Click **List for sale**.\r
3. If you own more than one copy, set **Quantity (you have N)**. Listing fewer than all of them splits those copies off into their own stack, so only the listed copies get picked later. Listing all of them lists the whole stack.\r
4. Check the **Price**. It starts at the card's market price.\r
5. Choose a **Channel**: **Manual**, **TCGplayer** or **eBay**.\r
6. Optionally add a **Note**.\r
7. Click **List for sale**.\r
\r
![The List for sale dialog](sales-list-for-sale.png)\r
\r
Choosing **eBay** adds an eBay section and publishes the listing on eBay. See [eBay](help:ebay).\r
\r
> [!TIP]\r
> In a binder, you can also open a card's menu and choose **List for sale** directly.\r
\r
### List many cards at once\r
\r
1. On the Collection page or a location page, click **Select** and tick the cards you want.\r
2. Click **List for sale** in the selection bar.\r
3. Choose a **Channel** and optionally add a **Note**, then click **List for sale**.\r
\r
Each selected card is listed as a whole stack at its current market price. Cards that are already listed are skipped. Fine-tune prices afterwards on the **Listings** tab.\r
\r
> [!NOTE]\r
> Bulk listing with the **eBay** channel only marks the cards as listed in OmniCard. It doesn't publish anything on eBay. To publish on eBay, list each card on its own. See [eBay](help:ebay).\r
\r
A card that's already listed shows **Already listed for sale** in its details. Unlist it from the **Listings** tab before listing it again.\r
\r
## Manage listings\r
\r
The **Listings** tab shows every active listing.\r
\r
![The Listings tab](sales-listings.png)\r
\r
| Column | Meaning |\r
|---|---|\r
| **Name** | The card name (✦ marks a foil) |\r
| **Set** | The set code |\r
| **Cond** | The card's condition |\r
| **Channel** | Manual, TCGplayer or eBay |\r
| **Qty** | How many copies are listed |\r
| **Price** | The listed price per copy |\r
| **Status** | **Listed** (waiting to be pulled) or **Picked** (pulled for sale) |\r
\r
The buttons at the end of each row:\r
\r
- **Mark picked (move to sales location)**: the check mark, shown on listings that are still **Listed**. See [Pick listed cards](help:sales#pick-listed-cards).\r
- **Edit**: change the listing's price, channel, quantity or note.\r
- **View on eBay**: opens the live eBay listing in a new tab. Shown only for cards published on eBay.\r
- **Update on eBay**: changes the live eBay listing. Shown only for cards published on eBay. See [eBay](help:ebay).\r
- **Unlist**: takes the card off the market. The card stays in your collection.\r
\r
### Edit a listing\r
\r
1. Click the **Edit** button on the listing's row.\r
2. In **Edit listing**, change the **Price**, **Channel**, **Quantity** or **Note**.\r
3. Click **Save**.\r
\r
> [!NOTE]\r
> Editing a listing changes it in OmniCard only. To change a card that's live on eBay, use **Update on eBay**.\r
\r
## Pick listed cards\r
\r
Picking means pulling listed cards from storage so they're ready to sell. When you mark a listing picked, its status changes to **Picked**. If your for-sale location is turned on, the card also moves to that location, so OmniCard always knows where it is.\r
\r
1. On the **Listings** tab, click **Pick list (PDF)** to download a printable list of the cards to pull and where each one is stored. The list follows the game selector at the top of the app.\r
2. Pull the cards.\r
3. Click the check mark on each listing you pulled, or click **Mark all picked (N)** to mark every listed card at once.\r
\r
If picking fails because no for-sale location is set, an error appears with a link to **Settings**. Set one up as described below.\r
\r
### Set up the for-sale location\r
\r
These options are on the **Administration** page, **Sales** tab:\r
\r
- **For-sale location**: the location picked cards move to. Click **Change** to pick one, or **Clear** to remove it.\r
- **Move picked cards to the for-sale location**: when on, marking a listing picked moves the card to the for-sale location. When off, picking only changes the status and the card stays where it is.\r
\r
![The Sales tab on the Administration page](sales-settings-for-sale-location.png)\r
\r
> [!TIP]\r
> Create a location called "For Sale" on the [Locations](help:locations) page and choose it here. Your pulled cards then stay together in one place until they ship.\r
\r
## Work with orders\r
\r
The **Orders** tab is a board. Each column, or lane, is a stage of the order, and each order is a card in a lane. A card shows the customer, the channel, the order number, the item count and the order total.\r
\r
The standard lanes are:\r
\r
| Lane | What it means |\r
|---|---|\r
| **Created** | A new order. You can still edit everything. |\r
| **Packed** | Packed and ready to ship. You can still edit everything. |\r
| **Shipped** | Sent. OmniCard records the sale and removes the sold cards from your collection. |\r
| **Completed** | Finished. |\r
| **Cancelled** | Called off. Nothing is removed from your collection. |\r
\r
Each lane's dot and the colored stripe on its order cards help you scan the board at a glance. The number next to a lane's name is how many orders it holds.\r
\r
> [!NOTE]\r
> Your board's lanes can be customized for your store, so lane names and colors may differ. Each lane still behaves like one of the stages above.\r
\r
### Create an order\r
\r
1. Click **New order**.\r
2. Choose a **Customer**. If the buyer isn't listed yet, add them on the **Customers** tab first.\r
3. Choose the **Channel** and optionally enter an **Order # (optional)**, such as a marketplace order number.\r
4. Click **Create**. The order's details panel opens so you can add cards.\r
\r
### Add cards to an order\r
\r
1. In the order's details panel, type at least two letters of a card name in **Add a card (search your collection)**. Results come from your collection and follow the game selector at the top of the app.\r
2. Each result shows the card's name, set and condition, plus a price box that starts at the market price. Change the price if needed.\r
3. Click **+** to add the card. Each click adds one copy.\r
\r
The **Items** heading shows the number of items and the order total. To remove a card, click the trash button on its line.\r
\r
![An order's details panel](sales-order-detail.png)\r
\r
> [!TIP]\r
> Building a big order? Leave the search text in place and click **+** on each result you need. The list stays open, so you can add several cards in a row.\r
\r
### Fill in order details\r
\r
The top of the details panel holds the order's details: **Channel**, **Order #**, **Tracking**, **Ship charged** (what the buyer paid for shipping), **Ship cost** (what shipping cost you), **Fees** (marketplace fees) and **Notes**. Click **Save header** to keep your changes.\r
\r
### Move an order through the board\r
\r
Drag an order's card and drop it on another lane. The move saves right away.\r
\r
When an order enters a shipped lane for the first time:\r
\r
- The sale is recorded, and the sold copies are removed from your collection.\r
- The cards' listings are marked sold.\r
- Any live eBay listing for those cards is ended, so they can't sell twice.\r
\r
> [!WARNING]\r
> Add every card to the order before you move it to **Shipped**. Cards added afterwards aren't removed from your collection automatically.\r
\r
### Edit a shipped or completed order\r
\r
Once an order is past **Packed**, its details panel is locked. A message says that header and line edits are locked until you move the order back to Created or Packed.\r
\r
To correct a mistake, drag the order back to **Created** or **Packed**, make your changes, then move it forward again.\r
\r
> [!WARNING]\r
> Moving a shipped order back doesn't return its cards to your collection, and moving it to **Shipped** again records the sale again. Only do this to fix order details, and check your collection afterwards.\r
\r
### Delete an order\r
\r
While an order is in **Created** or **Packed**, open it and click **Delete order**, then confirm. Shipped and completed orders can't be deleted. Move them to **Cancelled** instead if they didn't go through.\r
\r
## Print a receipt\r
\r
1. Click an order on the board to open its details.\r
2. Click **Print receipt**. A print-ready receipt opens in a new tab, sized for your receipt printer, and your browser's print dialog appears.\r
3. Or click **PDF** to download the receipt as a PDF file.\r
\r
An administrator sets the store name, address, logo, paper width, font size, footer text and whether prices are shown. These options are on the **Administration** page, **Receipts** tab. See [Administration](help:administration).\r
\r
![A printed receipt preview](sales-receipt.png)\r
\r
## Import orders from a CSV file\r
\r
Bring in orders from a marketplace export, such as a TCGplayer shipping export, instead of typing them in.\r
\r
1. On the **Orders** tab, click **Import CSV**.\r
2. **Choose file**: click **Choose CSV file…** and pick your file.\r
3. **Map columns**: tell OmniCard which column in your file holds each order field.\r
   - Pick a **Template**. **TCGPlayer Shipping Export (built-in)** is selected to start, and it already matches TCGplayer's columns.\r
   - Choose the **Channel** the orders came from.\r
   - For each field, pick the matching column from your file, or leave it **— not mapped —**.\r
   - **Order number** must be mapped. OmniCard uses it to recognize orders you've already imported.\r
4. Click **Preview**.\r
5. **Review & import**: check the rows. Untick any you don't want, then click **Import N orders**.\r
\r
![The Import orders from CSV dialog, mapping step](sales-import-orders-map.png)\r
\r
The fields you can map are **Full name**, **First name**, **Last name**, **Order number**, **Order date**, **Address line 1**, **Address line 2**, **City**, **State / province**, **Postal code**, **Country**, **Shipping fee paid**, **Item count**, **Value of products**, **Tracking number** and **Carrier**.\r
\r
In the review step, the **Status** column tells you what will happen to each row:\r
\r
| Status | Meaning |\r
|---|---|\r
| New customer · New order | Creates the order and a new customer |\r
| Matched customer · New order | Creates the order for an existing customer (matched by name and postal code), and updates that customer's address |\r
| Already imported | This order number already exists, so the row is skipped |\r
\r
Warnings, such as a date that couldn't be read, appear above the list. Imported orders start in the **Created** lane.\r
\r
> [!NOTE]\r
> Imported orders include the buyer, order number, date, shipping and tracking, but not the individual cards. Open an imported order and add cards from your collection if you want them removed from your collection when it ships.\r
\r
### Save your own import template\r
\r
If you import from a source whose columns don't match a template:\r
\r
1. Map the columns as described above.\r
2. Enter a **New template name** and click **Save as template**.\r
\r
Next time, pick your template from the **Template** list. To remove a template you saved, select it and click **Delete this template**. The built-in template can't be deleted.\r
\r
## Manage customers\r
\r
The **Customers** tab lists everyone you sell to, with their **Name**, **Email**, **Phone** and **Location** (city and state).\r
\r
![The Customers tab](sales-customers.png)\r
\r
- **Add a customer**: click **New customer**, fill in at least the **Name**, and click **Save**. You can also add email, phone and a full mailing address.\r
- **Edit a customer**: click the pencil button on their row.\r
- **Delete a customer**: click the trash button and confirm.\r
\r
Customers are also created automatically when you import orders from a CSV file.\r
\r
## Troubleshooting\r
\r
- **"Mark picked" shows an error about the for-sale location.** No for-sale location is set, or the one you chose was deleted. Set one on the **Administration** page, **Sales** tab, or turn off **Move picked cards to the for-sale location**.\r
- **I can't edit an order.** It's past **Packed**. See [Edit a shipped or completed order](help:sales#edit-a-shipped-or-completed-order).\r
- **A card won't list.** It's already listed. Find it on the **Listings** tab and click **Unlist** first.\r
- **My search in "Add a card" finds nothing.** Type at least two letters, and check that the game selector at the top of the app is set to the card's game or **All Games**.\r
\r
For more help, see [Troubleshooting](help:troubleshooting).\r
`,Ke=`# Saved views

A saved view remembers how you like a card list laid out: the search, the sort, rows per page, **Stack duplicates**, which columns show and in what order, and on a location page, table or stacked view and how stacks are grouped. Save as many views as you like and choose one to open by default.

## Where saved views work

Saved views are on the [Collection](/collection) page and on every location's page. The view button sits to the right of the search box and shows the name of the view in use. It shows **Default view** when you haven't picked a saved view.

![The saved views menu on the Collection page](saved-views-menu.png)

Each view belongs to:

- **One page.** A view saved on the Collection page is offered only there. A view saved on a location's page is offered only on that location. To use it on other locations too, [copy it](#copy-a-view-to-other-locations).
- **A game.** You choose this when you save. A view can be for the game selected in the top bar only, or for **Any game**. Views saved while **All Games** is selected show only when **All Games** is selected.
- **You.** Your views are private. Other people can't see them. Administrators can also publish [shared views](#shared-views-and-defaults-for-everyone) for everyone.

## What a view remembers

| Setting | Where you change it |
|---|---|
| Search | The search box. Press **Enter** to apply it. |
| Sort | Click a column header. |
| Rows per page | **Rows per page** at the bottom of the list. |
| Stack duplicates | The **Stack duplicates** switch. |
| Columns | The **Columns** button. See [Choose columns](#choose-columns). |
| Table or stacked view | The two buttons beside the search box on a location page. |
| Group by | **Group by** in stacked view. |

The page you're on, the cards you've selected and any open card details aren't part of a view.

## Choose columns

Click **Columns** in the toolbar above the list.

![The Columns menu with Rarity turned off](saved-views-columns.png)

- Untick a column to hide it, and tick it to show it again. **Name** always shows.
- Use the up and down arrows to move a column left or right in the list.
- Click **Show all in default order** to undo your column changes.

You can also hide a column from the menu on its header.

## Save a view

Change the layout until it looks the way you want. A dot appears on the view button when the layout has changes you haven't saved.

To save it as a new view:

1. Click the view button, then click **Save as new view…**.
2. Enter a **Name**.
3. Under **Show this view for**, choose the selected game only, or **Any game**.
4. Tick **Use as my default here** to open this page with the view from now on.
5. Click **Save**.

![The Save as new view dialog](saved-views-save-dialog.png)

To update the view you're using, click the view button, then click **Save changes**.

To throw away your changes, click **Discard changes**. The layout goes back to how the view was saved.

> [!TIP]
> The page address includes the view, for example \`/collection?view=12\`. Bookmark it to come back to that view, even if another view is your default. If the view was saved for a different game, OmniCard switches the top bar to that game.

## Switch views

Click the view button and pick a view. The menu lists:

- **Default view**: name A→Z, 100 rows per page, every column.
- **My views**: the views you've saved for this page.
- **Shared views**: views an administrator has published for everyone.

A ★ marks your default. A people icon marks the default for everyone. Views saved for any game say *Any game* under their name.

## Choose the view a page opens with

Pick a view, then click the view button and click **Set as my default**. The page opens with that view from now on, for the game the view belongs to. Click **Remove as my default** to stop.

You can have one default per page for each game, plus one any-game default. When you open a page, OmniCard uses the first of these that exists:

1. Your default for the selected game.
2. Your any-game default.
3. The default for everyone set by an administrator.
4. The **Default view**.

Setting an any-game view as your default replaces your default for the game you're looking at, so it's what opens next time. Your defaults for other games don't change.

## Rename or delete a view

Pick the view, then click the view button and choose **Rename…** or **Delete**. If you delete a view someone uses as their default, their page opens with the next default instead.

## Copy a view to other locations

A view saved on a location's page can be copied to other locations.

1. Open the location and pick the view.
2. Click the view button, then click **Copy to other locations…**.
3. Tick the locations to copy it to. Click a group heading to tick or untick the whole group, or use **Select all** and **Select none**.
4. Tick **Use it as my default on those locations** to make the copies open by default.
5. Click **Copy to N locations**.

![Copying a location's view to other locations](saved-views-copy-dialog.png)

If a location already has a view with the same name, its layout is replaced. Each copy is separate, so changing one later doesn't change the others.

## Shared views and defaults for everyone

Administrators can share views with everyone and choose what everyone sees first.

- In **Save as new view**, tick **Share with everyone**. On a location page, choose **This location only** or **All locations**. A view shared with all locations is offered on every location's page.
- Pick a shared view, then click **Set as default for everyone**. Everyone who hasn't chosen their own default for that page and game sees it. For a view shared with all locations, it applies on every location.
- Tick **Make it the default for everyone** when saving to do both at once.

Everyone can pick a shared view, but only administrators can save over it, rename it or delete it. To change a shared view for yourself, pick it, make your changes, and use **Save as new view…**.

> [!NOTE]
> A default for one location beats a default for all locations. If an administrator gives a location its own default, that location uses it.

## Troubleshooting

- **A view I saved is missing.** Check the game in the top bar. A view saved for one game only shows when that game is selected.
- **A location view isn't on another location.** Location views stay with their location. Use **Copy to other locations…**.
- **Save changes is greyed out.** You haven't changed anything since the view was saved, or it's a shared view and only administrators can change it.
- **Stack duplicates keeps changing.** The setting belongs to the view in use. Save the view after changing it.
`,Xe=`# Scan batches\r
\r
Scan batches let a scanner drop images into a watched folder on the server, where OmniCard matches them in the background. You then review each batch on the Scan page and add it to a location, just like an interactive scan.\r
\r
## How scan batches work\r
\r
With interactive scanning you upload pictures from your browser and wait while they match (see [Scanning cards](help:scanning)). Scan batches turn that around:\r
\r
1. Your scanner software saves its images into a folder that OmniCard watches. Each game has its own folder.\r
2. Each subfolder becomes one **batch**, named after the subfolder. Images saved directly in the game's folder (not in a subfolder) go into a batch named after today's date.\r
3. When no new file has arrived for a short while (the *quiet period*), OmniCard matches the batch's cards in the background. You don't need to have the browser open.\r
4. The batch appears on the [Scan](/scan) page. Someone opens it, reviews the matches, and adds the cards to a location.\r
\r
Unlike interactive scans, which live only in your browser, batches are stored on the server. Anyone with access to Scan can see them, and you can stop reviewing and pick up again later, even from a different device.\r
\r
After a file is picked up, the original is moved into a **_processed** subfolder of the game's folder, so it is never imported twice.\r
\r
## Set up watched folders (administrators)\r
\r
An administrator sets this up once in [Administration ▸ Scan Badges](/settings?tab=scan), in the **Watched scan folders** section:\r
\r
- **Watch folders** turns the feature on or off.\r
- **Quiet period (seconds)** is how long to wait with no new file before matching starts.\r
- **Keep images (days)** is how long stored scans of committed or discarded batches are kept before they are deleted.\r
- For each game, enter the **Folder on the server** and switch it **Active**. A chip shows **Found**, **Folder not found**, or **Can't move files** (OmniCard can see the folder but can't move files out of it).\r
- For each folder, choose the defaults every scan in it gets: **Sets (art fallback)**, **Condition**, **Card language**, **Foil**, and an optional **Default location** that is pre-selected when someone reviews a batch from that folder.\r
\r
Click **Save** when you are done. See [Administration](help:administration) for more about settings.\r
\r
![The Watched scan folders settings with a folder path, its Found status and batch defaults](administration-scan-folders.png)\r
\r
> [!NOTE]\r
> The folder must be on (or reachable from) the server that runs OmniCard, not on your own computer, unless they are the same machine.\r
\r
## Find waiting batches\r
\r
When batches are waiting, a number appears on the **Scan** item in the navigation menu. It counts open batches that nobody is reviewing yet.\r
\r
On the [Scan](/scan) page, the **Scan batches** panel lists every open batch, plus batches closed in the last day. Click the panel's header to show or hide it; chips in the header such as *2 ready* and *1 in progress* stay visible even when it is collapsed. The panel is hidden when there are no batches.\r
\r
Each batch shows:\r
\r
- Its name, its game, and a status chip:\r
\r
| Status | Meaning |\r
|---|---|\r
| **Waiting for files** | Files are still arriving. Matching starts after the quiet period. |\r
| **Matching N of M** | Cards are being matched. A progress bar shows how far along it is. |\r
| **Ready** | Every card has been matched and the batch is ready to review. |\r
| **Committed** | The batch was added to your collection. |\r
| **Discarded** | The batch was thrown away. |\r
\r
- How many cards it holds, how many errors and how many committed cards it has, and when its last file arrived.\r
- Who is reviewing it: *You're reviewing this* or *In review by* someone else.\r
\r
![The Scan batches panel with one Ready batch and one batch still matching](scan-batches-panel.png)\r
\r
## Review a batch\r
\r
1. In the **Scan batches** panel, click **Open** on the batch. (If you were already reviewing it, the button reads **Continue**.)\r
2. The batch opens on its own page, titled *Batch: name*. You are now its reviewer, and nobody else can change it while you have it.\r
3. Review the cards exactly as you would on the Scan page: compare the scan with the matched art, **Confirm match** or **Search catalog** to correct it, set condition, foil, tags and other properties, and use the filters and bulk **Edit**. See [Review the list](help:scanning#review-the-list) and [Correct a wrong match](help:scanning#correct-a-wrong-match).\r
4. Check that **Add to location…** shows the right location. If the folder has a default location it is already selected; you can change it.\r
5. Click **Add N confirmed cards**.\r
\r
The bar at the top of the batch shows the game and the batch's fixed settings (*Sets*, *Condition*, *Language*, *Foil*) taken from the folder. You can't add more pictures to a batch from the browser; save more files into the folder instead.\r
\r
Your changes are saved to the server as you work. You don't have to finish in one sitting: leave the page and come back later with **Continue**.\r
\r
![A batch open for review, with the batch settings bar and the review list](scan-batches-review.webp)\r
\r
### Adding cards while matching is still running\r
\r
You can add confirmed cards from a batch even while the rest is still matching; new matches appear in the list as they finish. Files that arrive late are added to the same open batch.\r
\r
### Fixing errors\r
\r
If some cards couldn't be matched because of an error, a **Retry N errors** button appears in the batch's settings bar. Click it to match those cards again.\r
\r
### Removing cards\r
\r
Click **Remove** on a card to take it out of the batch. Its stored image is deleted from the server. The original file stays in the **_processed** subfolder.\r
\r
### When the batch is finished\r
\r
Once every card in a batch has been added or removed, the batch closes and you return to the Scan page. It shows as **Committed** (or **Discarded** if nothing was added) in the panel for a day, then disappears from the list.\r
\r
## One reviewer at a time\r
\r
Only one person can review a batch at a time, so two people never add the same cards twice.\r
\r
- **Open** claims the batch for you. Following a link to a batch nobody is reviewing claims it automatically.\r
- **Release** gives the batch up so someone else can take it. Your saved changes stay with the batch. The reviewer or an administrator can release it.\r
- If someone else is reviewing a batch, the panel offers **View** instead of **Open**. The batch opens read-only with the message that that person is reviewing it; you can look but not change anything.\r
- An administrator viewing someone else's batch can click **Take over** to become its reviewer.\r
- If a batch you opened has no reviewer (for example after you released it), click **Review this batch** to start reviewing it again.\r
- If someone takes the batch over while you have it open, you'll see a warning that you're no longer reviewing it, and your last changes may not have been saved.\r
\r
## Discard a batch\r
\r
To throw a whole batch away, click **Discard** in the panel or at the top of the batch page, then confirm in the **Discard batch?** window. Its unsaved cards and their stored images are deleted. The original files stay in the folder's **_processed** subfolder, so you can move them back out to scan them again.\r
\r
The reviewer or an administrator can discard a batch. A batch nobody is reviewing can be discarded by anyone who is allowed to add scans to the collection.\r
\r
> [!WARNING]\r
> Discarding can't be undone from within OmniCard.\r
\r
## Tips and troubleshooting\r
\r
- **A batch never appears.** Ask an administrator to check that **Watch folders** is on, the game's folder is **Active**, and its status chip reads **Found**. Files must be JPEG, PNG or TIFF images.\r
- **A batch stays on Waiting for files.** Matching starts only after no new file has arrived for the quiet period. If scanning is still going, that's expected.\r
- **Batches only process while someone is using the site.** On some servers, background work pauses when the site is idle. An administrator can fix this; the settings page includes a hint about it.\r
- **Start a new batch.** Save into a new subfolder. Files saved into a subfolder whose batch is still open join that batch; once that batch is closed, new files in the same subfolder start a fresh batch, and a number such as *(2)* may be added to its name to tell them apart.\r
- **Wrong set or condition for the whole batch?** Use bulk **Edit** to change the cards' properties before adding them. To change the defaults for future batches, ask an administrator.\r
`,Ze=`# Scanning cards\r
\r
Use the Scan page to identify cards from photos or scanner images, check each match, fix any that are wrong, and add the confirmed cards to a location in your collection.\r
\r
## What the Scan page does\r
\r
Open [Scan](/scan) from the navigation menu. You add pictures of your cards (from a scanner, a phone camera, or a webcam), and OmniCard identifies each one against the selected game's card catalog. Every picture becomes a row in the review list. You look over the matches, correct any mistakes, set details like condition and foil, and then add the cards to a location.\r
\r
Nothing is added to your collection until you click the add button. Until then the scans live only in your browser, so you can take your time.\r
\r
![The Scan page with a review list on the left and the selected card's details on the right](scanning-overview.webp)\r
\r
> [!NOTE]\r
> If you don't see **Scan** in the menu, ask an administrator for access.\r
\r
## Scan your first cards\r
\r
1. Open [Scan](/scan).\r
2. In the top panel, choose the **Game** you are scanning.\r
3. Optionally set the **Condition**, **Card language** and **Foil** that most of these cards share. Each new scan starts with these values (see [Session defaults](#session-defaults)).\r
4. Add pictures with **Take photo**, **Use webcam** or **Add images** (see [Ways to add pictures](#ways-to-add-pictures)).\r
5. Wait for matching to finish. Each row shows a spinner while it is being matched, then a confidence percentage.\r
6. Select each row and compare **Uploaded scan** with **Matched art**. Click **Confirm match** when it is right, or **Search catalog** to pick the correct card.\r
7. Click **Add to location…** and choose where the cards are going.\r
8. Click **Add N confirmed cards**.\r
\r
Only cards that are both **confirmed** and **checked** are added. Everything else stays in the list.\r
\r
## Ways to add pictures\r
\r
| Button | Best for | What happens |\r
|---|---|---|\r
| **Take photo** | Phones and tablets | Opens the device's rear camera so you can photograph one card. On a computer it opens a normal file picker instead. |\r
| **Use webcam** | A computer with a webcam | Opens a live camera view that finds the card and captures it automatically. |\r
| **Add images** | Scanner output or saved photos | Lets you pick one or many image files at once. JPEG, PNG and TIFF files are supported. |\r
\r
You can mix all three in one session. New scans appear at the top of the list.\r
\r
### Scanning with a webcam\r
\r
1. Click **Use webcam**. The **Scan with webcam** window opens. If your browser asks for permission to use the camera, allow it.\r
2. If you have more than one camera, choose it from the **Camera** list.\r
3. Hold a card in front of the camera on a plain, contrasting background. An outline appears around the card when it is found.\r
4. Keep the card still. The status changes from **Point a card at the camera** to **Hold steady…**, and after about a second the card is captured: the frame flashes and the status reads **Captured ✓ — present the next card**.\r
5. Take the card away and present the next one. A card left sitting in view is captured only once.\r
6. To capture the same card again, click **Capture now**.\r
7. Click **Done** when you are finished.\r
\r
The window shows how many cards you have captured and thumbnails of the last few. Each capture is straightened and cropped automatically, then sent for matching just like an uploaded image.\r
\r
![The Scan with webcam window with a card outlined and the captured count below](scanning-webcam.png)\r
\r
> [!TIP]\r
> Most browsers only allow webcam access when OmniCard is opened over a secure (https) address or on the same computer that runs it. If the camera won't start, use **Take photo** on a phone or **Add images** instead, or ask your administrator.\r
\r
## Session defaults\r
\r
The top panel controls apply to every scan you add **after** you set them:\r
\r
- **Game** — which game's catalog to match against. Changing the game clears the set and language choices.\r
- **Sets (art fallback)** — pick one or more sets if you know what you are scanning. Matching is limited to those sets, and so is the catalog search when you correct a card. Leave it on **All sets** to search everything.\r
- **Condition** — NM, LP, MP, HP or DMG.\r
- **Card language** — **Auto (detect)** reads the language printed on Magic: The Gathering and Yu-Gi-Oh! cards. For other games, pick the language of the cards you are scanning.\r
- **Foil** — tick this before scanning foil cards. It marks the new scans as foil, helps matching cope with foil shine, and shows the foil market value.\r
\r
Changing these later does not change cards already in the list. To change existing cards, edit them individually or in bulk (see [Card properties](#card-properties)).\r
\r
## How matching works\r
\r
OmniCard compares each picture with the catalog's card artwork and also reads the printed text on the card, such as the set code and collector number, to pin down the exact printing. A card scanned upside down is usually still recognized.\r
\r
The colored chip on each row tells you how sure the match is:\r
\r
| Chip | Meaning |\r
|---|---|\r
| Green percentage (50% or more) | A strong match. Usually correct, but still worth a glance. |\r
| Amber percentage (15% to 49%) | A possible match. Check it carefully. |\r
| Red percentage (under 15%) | A weak match. Likely wrong. |\r
| **No match** | Nothing close enough was found. Use **Search catalog**. |\r
| **Corrected** | You picked the card yourself from the catalog. |\r
| **Error** | The picture couldn't be processed. Try a clearer image. |\r
\r
Matched cards are checked automatically; unmatched ones are not. When you confirm or correct a card, OmniCard remembers that identity, so the same card tends to match better next time.\r
\r
## Review the list\r
\r
On a wide screen the review list is on the left and the selected card's details are on the right. On a phone the details appear below the list.\r
\r
Each row shows the scan thumbnail next to the matched artwork, the card name, set and collector number, a one-line summary of its properties (condition, language, foil, quantity, tags, and a note icon), and its badges. A green check mark before the name means you have confirmed it.\r
\r
Click a row to see it in the detail panel:\r
\r
- **Uploaded scan** and **Matched art** side by side, so you can compare them.\r
- **View scan** opens a large version of your picture so you can read small print.\r
- The card's name, set, collector number and rarity.\r
- **Confirm match** marks the match as correct (the button then reads **Looks correct**). Confirming also checks the card.\r
- **Search catalog** opens the correction search.\r
- **Exclude from commit** / **Include in commit** unchecks or checks the card.\r
- **Remove** deletes the scan from the list.\r
\r
![The detail panel comparing the uploaded scan with the matched art, with Confirm match and Search catalog buttons](scanning-detail-panel.webp)\r
\r
### Checking cards\r
\r
The checkbox on each row decides whether the card is included when you add or export. Only cards with a match (or a correction) can be checked.\r
\r
- **Shift**-click a second checkbox to check or uncheck every row between it and the last one you clicked.\r
- The header above the list has a check-all box plus **All**, **None** and **Invert**, and shows how many are checked.\r
- **Confirm N checked** in the action bar confirms every checked, matched card at once. Use it after you have eyeballed a batch of strong matches.\r
\r
## Correct a wrong match\r
\r
1. Select the card and click **Search catalog**.\r
2. Type at least two letters of the card's name, or enter its **Collector #**, or both.\r
3. Hover over a result's small picture to see it larger.\r
4. Click the correct result.\r
\r
The card is now marked **Corrected**, and it is confirmed and checked. Click **Cancel** to close the search without changing anything.\r
\r
The search looks only in the sets chosen in **Sets (art fallback)**; the line under the search box says which. If the right card isn't listed, clear that set selection at the top of the page and search again.\r
\r
> [!TIP]\r
> Corrected cards don't show a confidence percentage or value badges, because those come from the automatic match.\r
\r
## Card properties\r
\r
The **Card properties** section of the detail panel sets the details for that copy:\r
\r
- **Condition** — NM, LP, MP, HP or DMG.\r
- **Language** — the language of this copy. A small language code (for example *JA*) appears on the row for non-English cards; it is filled in solid when the language was read from the card itself.\r
- **Quantity** — how many identical copies this scan represents.\r
- **Purchase price** — what you paid, if you want to track it.\r
- **Foil** — turn on for a foil copy, then optionally choose or type a **Foil type**.\r
- **Tags** — pick existing tags or type new ones.\r
- **Note** — free text, for example *signed, played, misprint…*\r
\r
### Edit several cards at once\r
\r
1. Check the cards you want to change.\r
2. Click **Edit** in the list header. The **Edit N selected cards** window opens.\r
3. Tick each property you want to apply, then set its value. Unticked properties are left alone.\r
4. For **Tags**, choose a **Mode**: **Add** keeps each card's existing tags and adds yours; **Replace** swaps them for yours.\r
5. Click **Apply to N**.\r
\r
![The Edit selected cards window with Condition and Tags ticked](scanning-bulk-edit.png)\r
\r
## Filter and sort the list\r
\r
When the list is long, use the filter bar above it:\r
\r
- **Filter by name** — shows rows whose card name contains the text.\r
- **Show** — **All**, **Checked only** or **Unchecked only**.\r
- **Min confidence %** — hides weaker matches.\r
- **Min value** — hides cards below a market value.\r
- **Sort by** — **None** (newest first), **Name**, **Confidence** or **Market value**. The arrow next to it switches between ascending and descending. Cards without a value sort to the bottom.\r
\r
While a filter or sort is on, you'll see *Showing X of Y* and a **Clear** button.\r
\r
> [!NOTE]\r
> Everything in the action bar works only on the cards you can currently see. **Confirm**, **Add**, **Export**, check-all and bulk **Edit** all ignore rows hidden by a filter.\r
\r
## Value badges\r
\r
Matched cards can show two kinds of badge:\r
\r
- A **gold star** means the card is new: you don't own a copy yet.\r
- One to five **currency signs** (for example $$$) show the card's market value tier. More signs means a more valuable card. Hover over them to see the exact value and the tier's price range.\r
\r
An administrator sets the currency and the price range for each tier in **Administration ▸ Scan Badges**. See [Administration](help:administration).\r
\r
## Add cards to a location\r
\r
1. Click **Add to location…** in the action bar. The **Add scanned cards to location** window opens.\r
2. Search or scroll the list. Locations are grouped by type and show their card count. Only locations you are allowed to change are listed.\r
3. Click a location to choose it. Its name now appears on the button.\r
4. Click **Add N confirmed cards**.\r
\r
The button stays disabled until a location is chosen, at least one card is confirmed and checked, and every scan has finished matching. When the cards are added you'll see *Added N card(s) to your collection*, and those rows leave the list. Unchecked or unconfirmed scans stay so you can deal with them later.\r
\r
### Create a location on the spot\r
\r
If the location doesn't exist yet, you don't have to leave the page:\r
\r
1. In the location window, click **New location**.\r
2. Enter a **New location name**. You'll be warned if *This name is already in use*.\r
3. Choose the **Type** (and the site, if your account can use more than one). For a deck box, also choose its game.\r
4. Click **Create & select**.\r
\r
![The Add scanned cards to location window with the New location form open](scanning-location-picker.png)\r
\r
See [Locations](help:locations) for more about location types and sites.\r
\r
## Export scans without adding them\r
\r
You can download your scans as a file without putting anything in your collection, for example to price a stack before you buy it.\r
\r
1. Check the cards you want to export. Confirming isn't required.\r
2. Click **Export (N)** and choose a format: **OmniCard (full detail)**, **TCGplayer**, **Moxfield**, **ManaBox**, **Archidekt**, **Deckbox**, **Dragon Shield**, **Card Price Ticker** or **Text list (.txt)**. Moxfield, ManaBox, Archidekt and Deckbox are marked *MTG only* and are available only for Magic: The Gathering.\r
3. Or choose **Copy as text list** to copy lines such as *4 Lightning Bolt (2X2) 117* to the clipboard, ready to paste into a deck builder.\r
4. Or choose **Export several formats (.zip)…**, tick the formats you want, and click **Download N formats**. Your choice is remembered for next time in this browser.\r
\r
Exported scans stay in the list, so you can still add them to a location later.\r
\r
> [!NOTE]\r
> If you don't see **Export**, ask an administrator for access.\r
\r
## Pick up where you left off\r
\r
Your scan list is saved in this browser as you work. If you refresh the page, close the tab or your browser crashes, the next visit to Scan shows *Restored N scans from your last session*. Scans that were still matching are matched again automatically. Click **Discard** in that message to throw the restored scans away.\r
\r
The saved list belongs to this browser on this device only. It isn't kept in a private or incognito window, and another person or device won't see it.\r
\r
## Game-specific notes\r
\r
- **Magic: The Gathering — The List.** When a scanned card is a reprint from *The List*, OmniCard matches it to that cheaper printing and shows a **The List** chip. If it shows **The List?** instead, the reprint was detected but its printing wasn't found in the catalog, so the printing and price shown may be the more expensive original. Check it before adding.\r
- **Yu-Gi-Oh! editions.** When the scan shows *1st Edition* or *Limited Edition* text, that edition appears as a chip in the detail panel for reference.\r
- **Riftbound Battlefields.** Landscape Battlefield cards can be scanned sideways; OmniCard turns them the right way automatically.\r
- **Language detection.** With **Card language** set to **Auto (detect)**, the printed language is read from Magic: The Gathering and Yu-Gi-Oh! cards.\r
\r
## Tips and troubleshooting\r
\r
- **Lots of wrong matches?** Choose the set or sets you are scanning in **Sets (art fallback)** before adding pictures. Make sure the right **Game** is selected.\r
- **Foils matching badly?** Tick **Foil** before you scan them.\r
- **Can't read the card in the thumbnail?** Use **View scan** to open the full picture.\r
- **The add button stays grey.** Check that a location is chosen, that matching has finished for every row, and that at least one card is both confirmed and checked and visible under the current filter.\r
- **A scan shows Error.** Remove it and try a sharper, well-lit picture.\r
- Scanner output can also be processed in the background without opening the browser. See [Scan batches](help:scan-batches).\r
- To check that a location holds exactly what you think it does, scan it in an audit. See [Auditing a location](help:location-audit).\r
- Added cards appear in your [collection](help:collection) right away.\r
`,en='# Search syntax\r\n\r\nOmniCard\'s search boxes understand a Scryfall-style search language: plain words for card names, plus fields like `t:creature`, `set:dom` or `tag:trade` that you can combine with `or`, `-` and parentheses. This topic lists every field and operator for each game.\r\n\r\n## Where you can use it\r\n\r\nThe same language works in two kinds of search box:\r\n\r\n- **Collection search** looks through the cards you own. You\'ll find it on the [Collection](/collection) page, on a location\'s page, and in a binder\'s Unplaced pool. On the Collection and location pages, press **Enter** to run the search. See [Collection](help:collection).\r\n- **Card search** looks through the full card catalog for a game, including cards you don\'t own. It\'s used when you correct a scan match and when you add a card to a location, binder pocket or list. See [Scanning](help:scanning).\r\n\r\nMost fields work in both. Some Magic fields only work in card search, and fields about your own copies (tags, condition, location) only work in collection search. The tables below say which.\r\n\r\n> [!TIP]\r\n> Click the **Search syntax help** icon (**?**) at the right of a collection search box to see the fields for the game selected in the top bar, each with an example you can copy.\r\n\r\n![The search syntax help popover listing fields and examples](search-syntax-help-popover.png)\r\n\r\n## The basics\r\n\r\n| You type | What it finds |\r\n|---|---|\r\n| `bolt` | Cards whose name contains "bolt". |\r\n| `lightning bolt` | Names containing both "lightning" and "bolt". |\r\n| `"lightning bolt"` | Names containing the exact phrase. |\r\n| `!"Lightning Bolt"` | Cards named exactly Lightning Bolt. |\r\n| `t:dragon` | Cards whose type contains "dragon". |\r\n| `t:dragon c:r` | Both must match (a space means AND). |\r\n| `t:dragon or t:angel` | Either one can match. |\r\n| `-is:foil` | Excludes matching cards. |\r\n| `(t:goblin or t:elf) c:g` | Parentheses group terms. |\r\n\r\nThings to know:\r\n\r\n- Searches ignore upper and lower case.\r\n- Put quotes around a value that contains spaces, for example `t:"legendary creature"` or `loc:"red binder"`.\r\n- `or` can be typed as `or` or `OR`.\r\n- A field name the game doesn\'t recognize is treated as a name search.\r\n\r\n## Operators\r\n\r\nPut an operator between the field name and the value, with no spaces: `cmc>=3`.\r\n\r\n| Operator | Meaning | Example |\r\n|---|---|---|\r\n| `:` | Contains, or "has" for colors and flags | `t:elf` |\r\n| `=` | Exactly equals | `cond=nm` |\r\n| `!=` | Doesn\'t equal | `set!=dom` |\r\n| `<` | Less than | `cmc<3` |\r\n| `>` | Greater than | `hp>100` |\r\n| `<=` | Less than or equal | `level<=4` |\r\n| `>=` | Greater than or equal | `r>=rare` |\r\n| `-` before a term | Not | `-tag:trade` |\r\n| `not:` | Same as `-is:` | `not:foil` |\r\n\r\nThe comparison operators (`<`, `>`, `<=`, `>=`) only work on fields that hold numbers or ordered values. The tables below mark these fields with "supports < >".\r\n\r\n## Fields for every game\r\n\r\nThese fields work in collection search for every game.\r\n\r\n| Field | Short forms | Example | What it matches |\r\n|---|---|---|---|\r\n| name | `n` | `name:bolt` | Card name (bare words do the same). |\r\n| set | `s`, `e`, `edition` | `set:dom` | Set code, exactly. |\r\n| cn | `number` | `cn:123` | Collector number, exactly. |\r\n| type | `t` | `t:creature` | Words in the card\'s type. |\r\n| rarity | `r` | `r:rare` | Rarity. Supports < > for Magic rarities. |\r\n| color | `c`, `id`, `ci`, `identity`, `commander` | `c:wu` | Magic colors (see below). |\r\n| condition | `cond` | `cond:nm` | Your copy\'s condition. |\r\n| lang | `language` | `lang:ja` | Your copy\'s language. |\r\n| location | `loc` | `loc:binder` | The name of the location the card is in. |\r\n| tag | `tags` | `tag:trade` | One of your tags on the card. |\r\n| is | `not` | `is:foil` | Flags on your copy (see below). |\r\n| foil | | `foil:true` | Foil (`true`) or non-foil (`false`). |\r\n\r\n### Details\r\n\r\n- **Rarity order.** For Magic, `r>=rare` finds rares and mythics, and `r<rare` finds commons and uncommons. The order is common, uncommon, rare, mythic. For other games, use the rarity name, for example `r:"super rare"`.\r\n- **Language.** Use a code such as `en`, `ja`, `de`, `fr`, `it`, `es`, `pt`, `ko`, `ru`, `zhs` or `zht`. Many spellings also work, such as `lang:jp` or `lang:japanese`. Cards with no language set count as English.\r\n- **Tags.** `tag:foo` matches any tag containing "foo". `tag=foo` matches the tag "foo" exactly. `-tag:foo` finds cards without it.\r\n- **Location.** `loc:binder` matches every location with "binder" in its name. Use `loc="Red Binder"` for one exact location.\r\n\r\n### is: flags\r\n\r\n| Flag | Finds |\r\n|---|---|\r\n| `is:foil` | Foil copies. |\r\n| `is:missing` | Copies flagged as missing, for example by an audit. |\r\n| `is:missingdb` | Copies flagged because the card couldn\'t be found in the card catalog. |\r\n\r\nUse `-is:foil` or `not:foil` for the opposite.\r\n\r\n### Magic colors\r\n\r\nColors use the letters W (white), U (blue), B (black), R (red) and G (green), or the words `white`, `blue`, `black`, `red`, `green`.\r\n\r\n| You type | Finds |\r\n|---|---|\r\n| `c:r` | Cards that include red. |\r\n| `c:wu` or `c>=wu` | Cards that include white and blue (and maybe more). |\r\n| `c=wu` | Exactly white and blue. |\r\n| `c<=wu` | Only white, blue, or both. Nothing else. |\r\n| `c!=wu` | Anything except exactly white and blue. |\r\n| `c:colorless` or `c:c` | Colorless cards and lands. |\r\n| `c:multicolor` or `c:multi` | Cards with two or more colors. |\r\n\r\nIn collection search, `id:` behaves the same as `c:`. Card search treats them separately (see below).\r\n\r\n> [!NOTE]\r\n> The **price** and **date** fields appear in the help list but don\'t filter your collection yet. To find your most valuable cards, sort the list by the **Market** column instead.\r\n\r\n## Magic: The Gathering\r\n\r\n### Extra fields in collection search\r\n\r\nWhen Magic is selected in the top bar, collection search also understands these fields.\r\n\r\n| Field | Short forms | Example | What it matches |\r\n|---|---|---|---|\r\n| oracle | `o` | `o:"draw a card"` | Words in the rules text. |\r\n| fulloracle | `fo` | `fo:trample` | Rules text including reminder text. |\r\n| flavor | `ft` | `ft:goblin` | Words in the flavor text. |\r\n| artist | `a` | `a:"rebecca guay"` | Illustrator name. |\r\n| watermark | `wm` | `wm:azorius` | Watermark. |\r\n| cmc | `mv`, `manavalue` | `cmc>=7` | Mana value. Supports < >. |\r\n\r\nFor `cmc`, use `-cmc:3` rather than `cmc!=3`.\r\n\r\nMagic has more fields than these, such as `pow`, `kw` or `f:modern`. They\'re listed in the **?** help, but in collection search they\'re treated as a name search. Use them in card search instead.\r\n\r\n### Card search (full Scryfall syntax)\r\n\r\nWhen you look up a Magic card to add or to correct a scan, the search supports nearly all of [Scryfall\'s syntax](https://scryfall.com/docs/syntax).\r\n\r\n| Field | Short forms | Example | What it matches |\r\n|---|---|---|---|\r\n| name | `n` | `n:bolt` | Card name. |\r\n| set | `s`, `e`, `edition` | `s:dom` | Set code, or words in the set name. `set=` matches the code only. |\r\n| block | | `block:innistrad` | Words in the set name. |\r\n| st | `settype` | `st:masters` | Set type (expansion, masters, commander, …). |\r\n| cn | `number` | `cn>=300` | Collector number. Supports < >. |\r\n| type | `t` | `t:"legendary creature"` | Type line. |\r\n| oracle | `o` | `o:"~ deals 3"` | Rules text. `~` stands for the card\'s own name. |\r\n| fulloracle | `fo` | `fo:trample` | Rules text including reminder text. |\r\n| keyword | `kw` | `kw:flying` | Keyword ability. |\r\n| mana | `m`, `manacost` | `m:{2}{W}{W}` | Mana cost. `m:2ww` also works. `=` means exactly. |\r\n| cmc | `mv`, `manavalue` | `mv<=2` | Mana value. Supports < >. |\r\n| power | `pow` | `pow>=5` | Power. Supports < >, and `pow>tou`. |\r\n| toughness | `tou` | `tou<3` | Toughness. Supports < >. |\r\n| loyalty | `loy` | `loy>=5` | Starting loyalty. Supports < >. |\r\n| defense | `def` | `def>=4` | Battle defense. Supports < >. |\r\n| pt | `powtou` | `pt:2/2` | Power and toughness together. |\r\n| colors | `c`, `color` | `c:rg` | Card colors. |\r\n| identity | `id`, `ci`, `commander` | `id<=wu` | Color identity, for Commander. |\r\n| produces | | `produces:g` | Colors of mana the card can make. |\r\n| devotion | | `devotion>=3` | Number of colored mana symbols. Supports < >. |\r\n| rarity | `r` | `r>=rare` | Rarity. Supports < >. |\r\n| artist | `a` | `a:"rebecca guay"` | Illustrator. |\r\n| flavor | `ft` | `ft:goblin` | Flavor text. |\r\n| watermark | `wm` | `wm:azorius` | Watermark. |\r\n| has | | `has:watermark` | `watermark`, `indicator` or `flavor`. |\r\n| border | | `border:borderless` | Black, white, silver or borderless. |\r\n| frame | | `frame:showcase` | Frame year (`2015`) or effect (`showcase`, `extendedart`). |\r\n| stamp | | `stamp:acorn` | Security stamp. |\r\n| layout | | `layout:transform` | Card layout. |\r\n| game | | `game:arena` | Where it\'s available: paper, mtgo or arena. |\r\n| in | | `in:paper` | Same as `game:`. |\r\n| lang | `language` | `lang:ja` | Printing language. |\r\n| year | | `year>=2020` | Release year. Supports < >. |\r\n| date | | `date>=2024-01-01` | Release date. Supports < >. |\r\n| usd | | `usd<1` | US dollar price. Supports < >. |\r\n| eur | | `eur<1` | Euro price. Supports < >. |\r\n| tix | | `tix<5` | MTGO ticket price. Supports < >. |\r\n| edhrec | | `edhrec<1000` | EDHREC popularity rank (lower is more popular). |\r\n| format | `f`, `legal` | `f:modern` | Legal or restricted in a format. |\r\n| banned | | `banned:legacy` | Banned in a format. |\r\n| restricted | | `restricted:vintage` | Restricted in a format. |\r\n\r\nColors in card search work like the **Magic colors** table above, and also accept `m` for multicolor and a number for how many colors a card has, for example `c>=2`.\r\n\r\n#### is: flags in card search\r\n\r\nIn card search, `is:` describes the printing, not your copy.\r\n\r\n| Group | Flags |\r\n|---|---|\r\n| Finish | `is:foil`, `is:nonfoil`, `is:etched`, `is:glossy` |\r\n| Printing | `is:promo`, `is:reprint`, `is:firstprint`, `is:reserved`, `is:digital`, `is:booster`, `is:oversized`, `is:variation`, `is:hires` |\r\n| Art | `is:fullart`, `is:textless`, `is:spotlight` |\r\n| Mana | `is:colorless`, `is:multicolor` (or `is:gold`), `is:hybrid`, `is:phyrexian` |\r\n| Layout | `is:split`, `is:flip`, `is:transform`, `is:meld`, `is:leveler`, `is:dfc`, `is:mdfc`, `is:adventure`, `is:token` |\r\n| Card kind | `is:permanent`, `is:spell`, `is:land`, `is:creature`, `is:vanilla`, `is:commander` |\r\n| Other | `is:gamechanger`, `is:contentwarning`, `is:funny` |\r\n\r\n#### Ordering card search results\r\n\r\nAdd these anywhere in a card search to change the result order. They don\'t filter anything.\r\n\r\n| Directive | Values |\r\n|---|---|\r\n| `order:` | `name`, `cmc`, `power`, `toughness`, `loyalty`, `released`, `rarity`, `color`, `usd`, `eur`, `tix`, `edhrec`, `set`, `artist`, `cn` |\r\n| `direction:` | `asc` or `desc` |\r\n| `unique:` | `cards` (one result per card name), `art` (one per artwork) or `prints` (every printing, the default) |\r\n\r\nExample: `t:dragon order:usd direction:desc` lists the priciest dragons first.\r\n\r\n> [!NOTE]\r\n> `order:` only works in card search. To sort your collection, click a column header on the Collection page.\r\n\r\n## One Piece Card Game\r\n\r\n| Field | Short forms | Example | What it matches |\r\n|---|---|---|---|\r\n| cost | | `cost:4` | Play cost. |\r\n| power | `pow` | `power:5000` | Power. |\r\n| counter | `ctr` | `counter>=1000` | Counter value. Supports < >. |\r\n| life | | `life:5` | Leader life. |\r\n| attribute | `attr` | `attribute:slash` | Attribute (Slash, Strike, …). |\r\n| subtype | `sub`, `trait` | `subtype:straw` | Subtype or trait. |\r\n\r\n`cost` and `power` match the value you type. Only `counter` supports < >. In card search, `color:red` also finds cards by color, and `set:` matches the set code or set name.\r\n\r\n## Riftbound\r\n\r\n| Field | Short forms | Example | What it matches |\r\n|---|---|---|---|\r\n| domain | `d` | `domain:body` | Any of a card\'s domains. |\r\n| energy | | `energy>=4` | Energy cost. Supports < >. |\r\n| might | `m` | `might>=5` | Might. Supports < >. |\r\n| power | `pow` | `power>=3` | Power. Supports < >. |\r\n| supertype | `super` | `supertype:champion` | Supertype. |\r\n\r\nIn card search, `set:` matches the set code or set name, and `cn:` supports < >.\r\n\r\n## Pokémon\r\n\r\n| Field | Short forms | Example | What it matches |\r\n|---|---|---|---|\r\n| hp | | `hp>=200` | Hit points. Supports < >. |\r\n| stage | | `stage:basic` | Evolution stage. |\r\n\r\n## Yu-Gi-Oh!\r\n\r\n| Field | Short forms | Example | What it matches |\r\n|---|---|---|---|\r\n| attribute | `attr` | `attribute:dark` | Monster attribute (DARK, LIGHT, …). |\r\n| level | `lvl`, `rank` | `level>=8` | Level or Rank. Supports < >. |\r\n| atk | | `atk>=3000` | ATK. Supports < >. |\r\n| def | | `def>=2500` | DEF. Supports < >. |\r\n\r\n## Final Fantasy TCG\r\n\r\n| Field | Short forms | Example | What it matches |\r\n|---|---|---|---|\r\n| element | `e`, `el` | `element:fire` | Element. |\r\n| cost | | `cost>=5` | Casting cost. Supports < >. |\r\n| power | `pow` | `power>=8000` | Power. Supports < >. |\r\n| job | | `job:warrior` | Job. |\r\n| category | `cat` | `category:vii` | Category, for example VII or XIV. |\r\n\r\nElement shorthands: `f` Fire, `i` Ice, `l` Lightning, `w` Water, `wi` Wind, `ea` Earth, `li` Light, `d` Dark. So `e:f` finds Fire cards.\r\n\r\n> [!WARNING]\r\n> In Final Fantasy TCG, `e:` means element, not set. Use `s:` or `set:` to search by set.\r\n\r\nFor Pokémon, Yu-Gi-Oh! and Final Fantasy TCG, `<` and `>` compare numbers when both sides are numbers. In card search, `set:` matches the set code or set name.\r\n\r\n## Searching with All Games selected\r\n\r\nWhen **All Games** is selected in the top bar, collection search still works across every game, with two differences:\r\n\r\n- Game fields only work by their full name, such as `artist:`, `cmc>=3`, `element:fire` or `hp>=100`. Short forms that belong to one game (like `a:` or `mv`) are treated as a name search.\r\n- A field several games share, such as `power`, matches cards from each game that has it.\r\n\r\nSelect a single game for the full set of short forms.\r\n\r\n## Example searches\r\n\r\n| Search | Finds |\r\n|---|---|\r\n| `t:creature c=g cmc<=2` | Mono-green creatures with mana value 2 or less (Magic). |\r\n| `r>=rare -is:foil loc:bulk` | Non-foil rares and mythics in locations named "bulk". |\r\n| `tag:trade or tag:sell` | Cards tagged trade or sell. |\r\n| `lang:ja is:foil` | Japanese foil copies. |\r\n| `set:dom -cond:nm` | Dominaria cards that aren\'t Near Mint. |\r\n| `o:"draw a card" t:instant` | Instants that draw a card (Magic). |\r\n| `e:f cost>=5` | Fire cards costing 5 or more (Final Fantasy TCG). |\r\n| `hp>=200 stage:basic` | Basic Pokémon with 200+ HP. |\r\n| `level>=8 attr:dark` | Level 8+ DARK monsters (Yu-Gi-Oh!). |\r\n| `might>=5 domain:body` | Body cards with 5+ might (Riftbound). |\r\n| `t:dragon order:usd direction:desc` | Dragons, most expensive first (Magic card search). |\r\n\r\n## Troubleshooting\r\n\r\n- **No results.** Check that the right game is selected in the top bar, and that values with spaces are in quotes.\r\n- **A field seems to be ignored.** It may not apply where you\'re searching. For example, `kw:flying` only works in card search, and `tag:` only works in collection search.\r\n- **`set:` finds nothing.** In collection search, `set:` needs the set code, such as `set:dom`, not the set name.\r\n- **Numbers compare oddly.** Only fields marked "supports < >" compare by value. Others match the text you type.\r\n',nn=`# Sets\r
\r
The Sets page shows every card printed in a set and which ones you own, so you can track how close you are to completing it.\r
\r
## Open a set checklist\r
\r
1. Choose a game in the top bar. The Sets page needs a single game, not **All Games**.\r
2. Open [Sets](/sets) from the navigation menu.\r
3. Click the **Set** box and pick a set. Type part of the set name or code to narrow the list.\r
\r
The checklist for that set loads below.\r
\r
![The Sets page with a set selected, the completion bar, and the checklist](sets-checklist.png)\r
\r
> [!NOTE]\r
> If you see *Pick a game in the top bar to browse its sets*, the top bar is set to **All Games**. Choose a game there first.\r
\r
## Read your progress\r
\r
Above the checklist, a summary line shows the set name, how many different cards you own out of the total, and the percentage, for example *Dominaria — 182/269 owned (67.7%)*. The bar underneath fills as you complete the set.\r
\r
A card counts as owned if you have at least one copy of it, in any condition, foil or non-foil.\r
\r
## The checklist\r
\r
Every card in the set is listed in collector-number order.\r
\r
| Column | What it shows |\r
|---|---|\r
| No. | Collector number. |\r
| Name | Card name. Hover over it to see the card art. |\r
| Rarity | Printed rarity. |\r
| Owned | A green **×N** badge with how many copies you own, or a dash if you have none. |\r
| Normal | Current market price for a regular copy. |\r
| Foil | Current market price for a foil copy, if one exists. |\r
\r
Cards you don't own are shown faded, so the gaps in your set stand out.\r
\r
Click a column header to sort the checklist, for example by **Normal** price to see which missing cards cost the most. Large sets are split into pages. Use the controls at the bottom of the list to move between them.\r
\r
## What counts toward your total\r
\r
- Copies in all your locations count, wherever they are stored.\r
- Cards you've traded away don't count.\r
- Checklists list the English printings of each set.\r
\r
To see where your copies of a card are, search for it on the [Collection](/collection) page, for example \`set:dom cn:123\`. See [Collection](help:collection) and [Search syntax](help:search-syntax).\r
\r
## Troubleshooting\r
\r
- **The set list is empty.** The card catalog for that game hasn't been downloaded yet. An administrator can download it from **Administration ▸ Catalog Data**. See [Administration](help:administration).\r
- **Prices are blank.** No price is available for that card yet. Prices update when the catalog is refreshed.\r
- **A card I own shows as not owned.** Check that the card in your collection has the right set and collector number. Open it from the [Collection](/collection) page and compare it with the checklist.\r
`,tn=`# Trades\r
\r
Record the cards you trade away, including cards picked up at a show that were never in OmniCard. Note what you got in return, and keep a history with values and photos.\r
\r
## What trades are for\r
\r
When you trade cards with someone, the Trades page keeps your collection accurate. You build a trade from the cards you're giving away, then finalize it with a note about what you received. You can add the value you received and a photo too. OmniCard marks the cards as traded away and adds the trade to your history. The history shows whether each trade gained or lost value.\r
\r
Open it from **Trades** in the navigation menu, or go to [Trades](/trades).\r
\r
![The Trades page with a trade in progress above the trade history](trades-builder.png)\r
\r
> [!NOTE]\r
> If you don't see **Trades** in the menu, or you can't start, finalize, or cancel a trade, ask an administrator for access.\r
\r
## Make a trade\r
\r
### 1. Start the trade\r
\r
On [Trades](/trades), click **New trade** under **Start a trade**. The panel changes to **Trade in progress**.\r
\r
You can also start from any card. Open the card's details, for example by clicking it in [Collection](/collection), and click **Add to trade**. The card is added to your trade in progress, or a new trade starts. A message reminds you to finalize it on the Trades page.\r
\r
### 2. Add cards you own\r
\r
1. Type in **Add a card you own**. You can search by name, and narrow it with \`set:\` and \`cn:\`. For example, \`bolt set:2x2 cn:117\`.\r
2. Matching cards appear below the box, with set, number, condition, and market price.\r
3. Click a card to add it to the trade.\r
\r
Only cards at sites you're allowed to change appear in the search. A card that's already in the trade can't be added twice.\r
\r
### 3. Add cards that aren't in your collection\r
\r
At a card show, you might trade away something you picked up that day and never entered into OmniCard. You can still record it:\r
\r
1. Click **Add off-catalog card (card-show pickup)**.\r
2. Fill in **Card name (optional)** and **Estimated value (optional)**.\r
3. Click **Photo (optional)** to attach a picture. On a phone, this opens the camera.\r
4. Click **Add card**.\r
\r
Off-catalog cards show *(off-catalog)* after their name. Click **Hide off-catalog card** to close the form.\r
\r
### 4. Check the cards\r
\r
Each card in the trade shows its picture, set, number, and value. Cards from your collection also have a **TCGplayer** link to check the current price. To take a card out, click its remove icon.\r
\r
The header shows a running total, such as **Giving 3 cards · $42.50**. For cards from your collection, the value is the card's market price. For off-catalog cards, it's the estimated value you entered.\r
\r
### 5. Finalize the trade\r
\r
1. Under **Finalize**, describe what you got in **Note — what did you get?**\r
2. Optionally, enter **Value received (optional)**. OmniCard uses this to show whether the trade gained or lost value.\r
3. Optionally, click **Photo of received cards (optional)** to attach a picture of what you got.\r
4. Click **Finalize trade** and confirm.\r
\r
When you finalize, the trade is applied to your collection right away:\r
\r
- The cards you gave away are marked as traded. They no longer count toward your collection's value, and lists and new trades can't use them.\r
- The trade appears at the top of your **History**.\r
\r
> [!NOTE]\r
> Finalizing doesn't add the cards you received to your collection. Scan or import them as usual. See [Scanning](help:scanning) and [Importing](help:importing).\r
\r
## Cancel a trade\r
\r
To drop a trade in progress, click **Cancel trade** and confirm. Nothing has been applied yet, so your collection doesn't change.\r
\r
## Come back to a trade later\r
\r
A trade in progress is saved as you go. You can leave the Trades page and come back later, in the same browser, and pick up where you left off. If you use **Add to trade** on a card, the card goes into the same trade in progress.\r
\r
## Trade history\r
\r
Under **History**, the cards you've traded away are listed newest first. Each trade shows:\r
\r
- The first card's name, plus how many more cards were in the trade.\r
- A camera icon if you attached a photo of the received cards.\r
- The value change, if you entered a value received. It's green when you received at least as much as you gave, and orange when you received less.\r
- The date of the trade.\r
\r
Click a trade to expand it and see:\r
\r
- **Out**: the total value of the cards you gave away.\r
- **Received**: the value you entered, or a dash if you didn't enter one.\r
- Your note about what you got.\r
- **Traded away**: every card in the trade, with its set, number, finish, value, and *(off-catalog)* when it wasn't from your collection.\r
\r
![An expanded trade in the history showing values, the note, and the traded-away cards](trades-history.png)\r
\r
## Tips\r
\r
- Enter a **Value received** for every trade. Over time, the value changes show whether your trades are paying off.\r
- At a show, add photos from your phone as you trade. They're a handy record if a deal is questioned later.\r
- To find a card fast, search by set code and collector number, such as \`set:dom cn:123\`.\r
- For cards you sold instead of traded, use [Sales](help:sales).\r
`,an=`# Troubleshooting\r
\r
Answers to the problems people run into most often, from missing sections and wrong matches to missing prices, edit conflicts and camera trouble. Each answer starts with the message you might see, where there is one.\r
\r
## A section or button is missing\r
\r
*You don't have permission to view this section. Ask an administrator if you need access.*\r
\r
*You don't have permission to do that.*\r
\r
What you can see and do in OmniCard depends on your role and permissions. If a sidebar section, a tab on the Administration page, or a button such as **Delete** is missing or refuses to work, your account doesn't include that permission.\r
\r
- Ask an administrator to change your role or give you the permission. See [Administration](help:administration).\r
- Once they've saved the change, move to another page. Your sidebar and buttons update without signing out.\r
- Some things are for administrators only, whatever their role: managing users, roles and sites, watched scan folders, and changing receipt and scan badge settings.\r
\r
![The message shown when you open a section you don't have access to](troubleshooting-no-access.png)\r
\r
## You can see a site but can't change it\r
\r
*You only have read access to that site.*\r
\r
*This card is in …, which you can only view. Ask an administrator for write access to change it.*\r
\r
An administrator can give you **Read** or **Write** access to each site. With **Read** you can browse and search its locations and cards, and add them to lists, but you can't change them. On the Locations page these locations are marked **view only**.\r
\r
When moving a list's cards into place, you might also see *Some cards on this list would be moved out of a site you only have read access to.* Move those cards yourself somewhere you can write to, or ask for **Write** access.\r
\r
Ask an administrator for **Write** access to the site. See [Administration](help:administration).\r
\r
## Cards or locations seem to be missing\r
\r
Check these before assuming something is lost:\r
\r
- **The game selector.** The selector in the top bar filters most pages to one game. Choose **All Games** to see everything.\r
- **The site filter.** The Locations page has a **Site** filter. Choose **All Sites**.\r
- **Hide empty locations.** On the Locations page, empty locations are hidden while this switch is on.\r
- **The search box.** Clear any search text on the Collection page.\r
- **Site access.** If cards are in a site you don't have access to, you won't see them at all. Ask an administrator.\r
- **Trades.** Cards you've traded away no longer count as part of your collection once the trade is finalized. See [Trades](help:trades).\r
\r
## The Sets page says to pick a game\r
\r
*Pick a game in the top bar to browse its sets.*\r
\r
The Sets page shows one game at a time. Choose a game in the top bar instead of **All Games**. See [Sets](help:sets).\r
\r
## A card matched the wrong printing\r
\r
OmniCard recognizes a card by its artwork and, for most games, by reading the set code and collector number printed on it. Sometimes it picks the wrong card or the wrong printing of the right card. Fix it on the Scan page before you add the card:\r
\r
1. Select the card in the scan list.\r
2. Click **Search catalog**.\r
3. Type the card's name and pick the correct card and printing from the results.\r
4. The card is now marked **Corrected** and is already confirmed, so you can add it as usual.\r
\r
![Correcting a scan with Search catalog](troubleshooting-search-catalog.webp)\r
\r
OmniCard remembers your corrections when you add the cards, so it gets better at matching that card in future scans.\r
\r
> [!TIP]\r
> If the right printing doesn't show up in the search, check **Sets (art fallback)** at the top of the Scan page. When sets are chosen there, the search only looks inside those sets. Remove them to search every set.\r
\r
If you've already added the wrong card, open it in the [Collection](help:collection), delete it, and add the correct card. You can rescan it, or use **Add card** on its location. See [Scanning cards](help:scanning).\r
\r
## A scan says No match\r
\r
*No confident match. Use "Search catalog" to pick the correct card, or "View scan" to read it.*\r
\r
OmniCard couldn't recognize the card well enough to guess. Click **View scan** to see the photo, then **Search catalog** to pick the card yourself. Common causes:\r
\r
- **The wrong game is selected.** Check the **Game** at the top of the Scan page.\r
- **The set is too new.** Ask an administrator to run **Download catalog** for that game.\r
- **The photo is unclear.** See [Take better photos with your phone](help:troubleshooting#take-better-photos-with-your-phone).\r
- **Sets (art fallback) is set to the wrong sets.** Clear it to match against all sets.\r
- **The card is in another language** and the catalog only has English. An administrator can add languages under **Administration ▸ Catalog Data ▸ Languages to download**, for the games that offer them.\r
\r
## Prices are missing or look wrong\r
\r
Market prices come from each game's price source, and an administrator refreshes them on the server.\r
\r
- **Prices are out of date.** Ask an administrator to run **Update prices** for the game under **Administration ▸ Catalog Data**.\r
- **One card has no price.** Some printings, such as certain promos and older editions, simply don't have a price at the source. OmniCard can't fill those in. A card with no price shows *n/a* when you open it and counts as zero in totals.\r
- **A foreign card shows the English price.** Non-English printings rarely have their own price, so they use the English printing's price. Japanese Pokémon cards have their own prices.\r
- **Foil and non-foil prices differ.** Check that the card's **Foil** setting is right. Foil copies use the foil price.\r
- **Sealed product** has its own market price on the Inventory page. See [Inventory](help:inventory).\r
\r
See [Dashboard](help:dashboard) for how prices feed into your totals.\r
\r
## Card images are missing\r
\r
- **New cards with no picture.** The catalog may not have the newest set yet. Ask an administrator to run **Download catalog** for that game.\r
- **Images load slowly or not at all.** By default, card images can come from outside websites. An administrator can run **Download artwork** under **Administration ▸ Catalog Data** to keep a copy of every image on your server.\r
- **A few cards never get a picture.** Some cards have no image at the source.\r
\r
## Someone else changed this item\r
\r
*This item was changed by someone else. Reload and try again.*\r
\r
Another person, or another tab or device of yours, saved a change to the same card, order or record after you opened it. OmniCard stopped your save so it wouldn't overwrite their change.\r
\r
1. Reload the page.\r
2. Check the item's current details.\r
3. Make your change again and save.\r
\r
## Sign-in problems\r
\r
**Incorrect username or password.** Check your typing. Passwords are case-sensitive, so check Caps Lock too. If it still fails, ask an administrator to reset your password under **Administration ▸ Users**.\r
\r
**You keep getting signed out.** If you don't tick **Remember me** when you sign in, you're signed out when you close the browser. Ticking it keeps you signed in on that device for up to 30 days. Clearing your browser's cookies also signs you out.\r
\r
**Not authenticated. Please sign in.** Your session ended while the page was open. Reload the page and sign in again.\r
\r
**You forgot your password.** Ask an administrator to reset it. After signing in with the new password, you can change it yourself under **Administration ▸ Users**.\r
\r
**You're setting up a new server.** Sign in with the built-in **Admin** account (password \`admin\`), then change that password straight away.\r
\r
## Take better photos with your phone\r
\r
On a phone, **Take photo** on the Scan page opens the rear camera. For the best matches:\r
\r
- Photograph **one card per photo**.\r
- Fill most of the frame with the card, keeping the whole card in view.\r
- Hold the phone **straight above the card** so the card isn't skewed.\r
- Use **even light**. Avoid glare and reflections, especially on foils and cards in sleeves or top-loaders. Tilting the card slightly away from a lamp often helps.\r
- Put the card on a **plain, dark background** that contrasts with its border.\r
- Make sure the bottom of the card, where the set code and collector number are, is **in focus**. That's what OmniCard reads to find the exact printing.\r
- Before scanning a pile, set **Condition**, **Card language** and **Foil** at the top of the Scan page. Each new photo gets those settings.\r
\r
See [Scanning cards](help:scanning).\r
\r
## The webcam won't start\r
\r
*Camera permission was denied. Allow camera access in your browser and try again.* Click the camera or lock icon in the browser's address bar, allow the camera, then close and reopen **Use webcam**.\r
\r
*No camera was found. Connect a webcam and try again.* Check the webcam is plugged in and not in use by another app, such as a video call.\r
\r
*Could not start the camera.* Browsers only allow a live camera on secure (https) connections, or when OmniCard runs on the same computer. If you open OmniCard by a plain local network address, the live webcam may be blocked. Use **Take photo** or **Add images** instead, or ask whoever runs the server about a secure address.\r
\r
*Failed to load the image-processing engine.* Reload the page and try again. Your browser needs to be able to download it the first time.\r
\r
## An image was rejected\r
\r
*Only JPEG, PNG and TIFF images are accepted.* Save or export the photo as JPEG or PNG and try again. Some phones save photos in other formats by default.\r
\r
*Image exceeds 30 MB limit.* Use a smaller image, or a lower resolution on your scanner. Card photos don't need to be huge.\r
\r
## Your scan list disappeared\r
\r
*Restored … scans from your last session.*\r
\r
Scans you haven't added yet are kept in your current browser on this device, so a refresh or crash doesn't lose them. They aren't shared with other browsers or devices, and a private or incognito window can't keep them. To keep scans safe, add them to a location, or use **Export** to download them without adding them.\r
\r
Scans from a watched scanner folder work differently. They're stored on the server and listed under **Scan batches** on the Scan page. See [Scan batches](help:scan-batches).\r
\r
## Scan batch problems\r
\r
*You're no longer reviewing this batch (someone else took it over, or it was closed). Your last changes may not have been saved.* Only one person can review a batch at a time, and someone else clicked **Take over**, or the batch was closed. Reopen it from the Scan page and check your work.\r
\r
*Nobody is reviewing this batch. Start reviewing it to make changes.* Click **Review this batch** to claim it.\r
\r
**A batch never appears.** Check that the scanner saves into a subfolder of the game's watched folder, and wait for the quiet period to pass. An administrator can check the folder's status under **Administration ▸ Scan Badges**. See [Administration](help:administration) and [Scan batches](help:scan-batches).\r
\r
## A card can't be moved, split or edited\r
\r
*Already listed for sale. Unlist it from Sales ▸ Listings to change.*\r
\r
A card that's listed for sale is locked against changes that would affect the listing, such as splitting its stack. Remove the listing on **Sales ▸ Listings** first. See [Sales](help:sales).\r
\r
*This deck box only holds … cards.* A deck box is tied to one game, so you can't put another game's cards in it. See [Deck boxes](help:deck-boxes).\r
\r
## Imports fail\r
\r
*Couldn't fetch that deck URL. Supported: Moxfield, Archidekt (public decks).* Check the link is a public Moxfield or Archidekt deck. Private decks can't be read.\r
\r
**Some cards couldn't be found.** OmniCard lists them after the import. Check their spelling, or that the catalog has their set. When the exact printing isn't in the catalog, OmniCard uses another printing of the same card and tells you which.\r
\r
**Importing into a location changed nothing.** Imports started from a location page are all or nothing. If any line has a problem, nothing is imported and every problem is listed so you can fix the file and try again.\r
\r
See [Importing](help:importing).\r
\r
## Catalog jobs won't start\r
\r
*A catalog refresh is already running.* Only one catalog job runs at a time. Wait for it to finish. Its progress shows on the **Catalog Data** tab.\r
\r
**A job finished almost instantly.** That's usually fine. If little has changed since the last run, there's little to do. Look for the ✓ under **Recent**. A ✗ means it failed, and the message next to it says why.\r
\r
See [Administration](help:administration).\r
\r
## eBay errors\r
\r
*Connect to eBay first.* OmniCard isn't linked to your eBay account. An administrator, or someone with eBay permissions, can connect it under **Administration ▸ eBay**.\r
\r
*Listed locally, but the eBay push failed.* The card is listed in OmniCard but eBay rejected the listing. Read the error, fix the problem (often the seller settings), and try again. See [eBay](help:ebay).\r
\r
## Still stuck\r
\r
- Reload the page. Many temporary problems clear up after a refresh.\r
- Check you're on the right game and site.\r
- Ask your OmniCard administrator. If you are the administrator, check the **Recent** results on the **Catalog Data** tab, and the folder status under **Scan Badges**.\r
`,on="/app/assets/administration-catalog-data-BjeNaI2j.png",rn="/app/assets/administration-scan-folders-CS75rQqx.png",sn="/app/assets/administration-site-access-D6UivBxH.png",cn="/app/assets/administration-tabs-BpCoGDvV.png",dn="/app/assets/administration-tabs-BpCoGDvV.png",ln="/app/assets/binders-add-to-pocket-CfrpNCbi.png",hn="/app/assets/binders-edit-mode-2WRu345_.webp",pn="/app/assets/binders-spread-view-C21YWD10.webp",un="/app/assets/collection-card-details-CEnyiCh8.png",mn="/app/assets/collection-decklist-check-DfYRH2pK.png",gn="/app/assets/collection-overview-BT8LOkQk.png",yn="/app/assets/collection-select-actions-DeNskt8b.png",wn="/app/assets/dashboard-overview-BgdtwCHP.png",fn="/app/assets/deck-boxes-panel-DX0XHQ-4.png",bn="/app/assets/deck-boxes-stacked-view-D-RtwJGJ.webp",kn="/app/assets/getting-started-layout-DXPiQzZD.png",vn="/app/assets/getting-started-phone-menu-AejRhoOH.png",Cn="/app/assets/getting-started-sign-in-DnUx9vwZ.png",xn="/app/assets/help-home-DYGYTGEX.png",Tn="/app/assets/importing-page-CLlMp9uw.png",Sn="/app/assets/inventory-new-product-wyjY3g5p.png",An="/app/assets/inventory-page-D7A7yRt0.png",In="/app/assets/inventory-product-drawer-Ceku7CC-.png",_n="/app/assets/lists-add-card-dialog-MhQSjlN6.png",On="/app/assets/lists-find-in-collection-BXe0H6i7.png",Bn="/app/assets/lists-overview-B2IW5FTm.png",Ln="/app/assets/lists-put-cards-away-DKGvHszG.png",En="/app/assets/location-audit-button-_OdyGPQm.png",Pn="/app/assets/location-audit-page-DsxC_qed.webp",Mn="/app/assets/location-audit-summary-CKlkpN6V.png",Dn="/app/assets/locations-add-bar-BLkpDoqv.png",Nn="/app/assets/locations-detail-page-CdfBmgZM.png",Fn="/app/assets/locations-import-dialog-C_NxOs9C.png",Un="/app/assets/locations-page-overview-BnNQ8JST.png",Rn="/app/assets/locations-row-menu-o6_KiRtY.png",Wn="/app/assets/sales-customers-DVrg8-v6.png",jn="/app/assets/sales-import-orders-map-BS_aWYm3.png",Gn="/app/assets/sales-list-for-sale-BfBz83FT.png",Yn="/app/assets/sales-listings-Q6MD86Iw.png",Hn="/app/assets/sales-order-detail-Dqrg7sx7.png",Vn="/app/assets/sales-orders-board-z3m97wnu.png",zn="/app/assets/sales-receipt-BKyV_jeV.png",qn="/app/assets/sales-settings-for-sale-location-BvzarQMv.png",$n="/app/assets/saved-views-columns-D7esoFxb.png",Qn="/app/assets/saved-views-copy-dialog-BWsC2JHE.png",Jn="/app/assets/saved-views-menu-Br3T19VA.png",Kn="/app/assets/saved-views-save-dialog-DQXqWDvx.png",Xn="/app/assets/scan-batches-panel-BH8rVmPX.png",Zn="/app/assets/scan-batches-review-BatFmhN3.webp",et="/app/assets/scanning-bulk-edit-D_dyVKxA.png",nt="/app/assets/scanning-detail-panel-CkbIVz1U.webp",tt="/app/assets/scanning-location-picker-Ck5QBotk.png",at="/app/assets/scanning-overview-BrL5uZH0.webp",ot="/app/assets/search-syntax-help-popover-B7d99PGs.png",rt="/app/assets/sets-checklist-Cq7lxxc_.png",st="/app/assets/trades-builder-BzkRc2Uo.png",it="/app/assets/trades-history-O80-mo8S.png",ct="/app/assets/troubleshooting-no-access-Egsybxdj.png",dt="/app/assets/troubleshooting-search-catalog-DJ707UXg.webp",F=Object.assign({"./en-US/administration.md":Ue,"./en-US/binders.md":Re,"./en-US/collection.md":We,"./en-US/dashboard.md":je,"./en-US/deck-boxes.md":Ge,"./en-US/ebay.md":Ye,"./en-US/getting-started.md":He,"./en-US/importing.md":Ve,"./en-US/inventory.md":ze,"./en-US/lists.md":qe,"./en-US/location-audit.md":$e,"./en-US/locations.md":Qe,"./en-US/sales.md":Je,"./en-US/saved-views.md":Ke,"./en-US/scan-batches.md":Xe,"./en-US/scanning.md":Ze,"./en-US/search-syntax.md":en,"./en-US/sets.md":nn,"./en-US/trades.md":tn,"./en-US/troubleshooting.md":an}),lt=Object.assign({"./images/administration-catalog-data.png":on,"./images/administration-scan-folders.png":rn,"./images/administration-site-access.png":sn,"./images/administration-tabs.png":cn,"./images/administration-users.png":dn,"./images/binders-add-to-pocket.png":ln,"./images/binders-edit-mode.webp":hn,"./images/binders-spread-view.webp":pn,"./images/collection-card-details.png":un,"./images/collection-decklist-check.png":mn,"./images/collection-overview.png":gn,"./images/collection-select-actions.png":yn,"./images/dashboard-overview.png":wn,"./images/deck-boxes-panel.png":fn,"./images/deck-boxes-stacked-view.webp":bn,"./images/getting-started-layout.png":kn,"./images/getting-started-phone-menu.png":vn,"./images/getting-started-sign-in.png":Cn,"./images/help-home.png":xn,"./images/importing-page.png":Tn,"./images/inventory-new-product.png":Sn,"./images/inventory-page.png":An,"./images/inventory-product-drawer.png":In,"./images/lists-add-card-dialog.png":_n,"./images/lists-find-in-collection.png":On,"./images/lists-overview.png":Bn,"./images/lists-put-cards-away.png":Ln,"./images/location-audit-button.png":En,"./images/location-audit-page.webp":Pn,"./images/location-audit-summary.png":Mn,"./images/locations-add-bar.png":Dn,"./images/locations-detail-page.png":Nn,"./images/locations-import-dialog.png":Fn,"./images/locations-page-overview.png":Un,"./images/locations-row-menu.png":Rn,"./images/sales-customers.png":Wn,"./images/sales-import-orders-map.png":jn,"./images/sales-list-for-sale.png":Gn,"./images/sales-listings.png":Yn,"./images/sales-order-detail.png":Hn,"./images/sales-orders-board.png":Vn,"./images/sales-receipt.png":zn,"./images/sales-settings-for-sale-location.png":qn,"./images/saved-views-columns.png":$n,"./images/saved-views-copy-dialog.png":Qn,"./images/saved-views-menu.png":Jn,"./images/saved-views-save-dialog.png":Kn,"./images/scan-batches-panel.png":Xn,"./images/scan-batches-review.webp":Zn,"./images/scanning-bulk-edit.png":et,"./images/scanning-detail-panel.webp":nt,"./images/scanning-location-picker.png":tt,"./images/scanning-overview.webp":at,"./images/search-syntax-help-popover.png":ot,"./images/sets-checklist.png":rt,"./images/trades-builder.png":st,"./images/trades-history.png":it,"./images/troubleshooting-no-access.png":ct,"./images/troubleshooting-search-catalog.webp":dt}),G=[{key:"start",topics:["getting-started","dashboard"]},{key:"scanning",topics:["scanning","scan-batches","location-audit"]},{key:"collection",topics:["collection","saved-views","search-syntax","sets"]},{key:"organizing",topics:["locations","binders","deck-boxes"]},{key:"lists",topics:["lists","importing","trades"]},{key:"selling",topics:["inventory","sales","ebay"]},{key:"admin",topics:["administration","troubleshooting"]}],ht=G.flatMap(e=>e.topics);function pt(){const e=new Set;for(const t of Object.keys(F))e.add(t.split("/")[1]);return[...e]}function ut(e){const t=pt(),o=[],a=r=>{r&&!o.includes(r)&&o.push(r)};if(e){a(t.find(s=>s.toLowerCase()===e.toLowerCase()));const r=e.split("-")[0].toLowerCase();a(t.find(s=>s.toLowerCase()===r)),a(t.find(s=>s.toLowerCase().split("-")[0]===r))}return a(de),o}function mt(e,t){var l;const o=t.replace(/\r\n?/g,`
`).split(`
`);let a=e;const r=o.findIndex(d=>/^#\s+/.test(d));r>=0&&(a=o[r].replace(/^#\s+/,"").trim(),o.splice(r,1));const s=o.join(`
`).trim(),h=((l=s.split(/\n\s*\n/).map(d=>d.trim()).find(d=>d&&!/^(#|!\[|>|-|\d+\.|\|)/.test(d)))==null?void 0:l.replace(/\s+/g," "))??"",i=U(`${a}
${s}`).replace(/^\s*(#+|\||>|-|\d+\.)\s*/gm,"").replace(/\||:?-{3,}:?/g," ").replace(/\s+/g," ");return{id:e,title:a,summary:U(h),body:s,plainText:i,searchText:i.toLowerCase()}}function U(e){return e.replace(/!\[([^\]]*)\]\([^)]*\)/g,"$1").replace(/\[([^\]]+)\]\([^)]*\)/g,"$1").replace(/\*\*|`|^>\s*\[![A-Z]+\]/gm,"").replace(/(^|\s)\*([^*]+)\*/g,"$1$2")}const $=new Map;function gt(e){const t=e??"",o=$.get(t);if(o)return o;const a=ut(e),r=[];for(const s of ht){const h=a.find(i=>F[`./${i}/${s}.md`]!==void 0);h&&r.push(mt(s,F[`./${h}/${s}.md`]))}return $.set(t,r),r}function yt(e){return lt[`./images/${e.replace(/^\.?\/?(images\/)?/,"")}`]}function wt(e){return U(e).toLowerCase().replace(/[^a-z0-9]+/g,"-").replace(/^-+|-+$/g,"")}function ae(e,t){const o=t.toLowerCase().split(/\s+/).filter(Boolean);return o.length===0?[]:e.filter(a=>o.every(r=>a.searchText.includes(r))).map(a=>{const r=o.every(s=>a.title.toLowerCase().includes(s));return{topic:a,snippet:ft(a,o[0]),score:r?0:1}}).sort((a,r)=>a.score-r.score).map(({topic:a,snippet:r})=>({topic:a,snippet:r}))}function ft(e,t){const o=e.plainText,a=e.searchText.indexOf(t);if(a<0)return e.summary;const r=Math.max(0,a-60),s=Math.min(o.length,a+t.length+90);return`${r>0?"…":""}${o.slice(r,s).trim()}${s<o.length?"…":""}`}const bt=E(n.jsx("path",{d:"M19 5v14H5V5zm0-2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2m-4.86 8.86-3 3.87L9 13.14 6 17h12z"}),"ImageOutlined"),kt=E(n.jsx("path",{d:"M9 21c0 .55.45 1 1 1h4c.55 0 1-.45 1-1v-1H9zm3-19C8.14 2 5 5.14 5 9c0 2.38 1.19 4.47 3 5.74V17c0 .55.45 1 1 1h6c.55 0 1-.45 1-1v-2.26c1.81-1.27 3-3.36 3-5.74 0-3.86-3.14-7-7-7m2.85 11.1-.85.6V16h-4v-2.3l-.85-.6C7.8 12.16 7 10.63 7 9c0-2.76 2.24-5 5-5s5 2.24 5 5c0 1.63-.8 3.16-2.15 4.1"}),"LightbulbOutlined"),oe=/^(#{1,6})\s+(.*)$/,re=/^\s*(-{3,}|\*{3,}|_{3,})\s*$/,se=/^!\[([^\]]*)\]\(([^)\s]+)\)\s*$/,O=/^(\s*)([-*+]|\d+[.)])\s+(.*)$/,M=e=>e.match(/^\s*/)[0].length;function Q(e){return oe.test(e)||re.test(e)||se.test(e)||/^\s*>/.test(e)||/^\s*\|/.test(e)||O.test(e)}function D(e){return e.trim().replace(/^\|/,"").replace(/\|$/,"").split(new RegExp("(?<!\\\\)\\|")).map(t=>t.trim().replace(/\\\|/g,"|"))}function R(e){var r;const t=e.replace(/\r\n?/g,`
`).split(`
`),o=[];let a=0;for(;a<t.length;){const s=t[a];if(!s.trim()){a++;continue}const h=s.match(oe);if(h){const c=h[2].trim();o.push({kind:"heading",level:h[1].length,text:c,id:wt(c)}),a++;continue}if(re.test(s)){o.push({kind:"hr"}),a++;continue}const i=s.match(se);if(i){o.push({kind:"image",alt:i[1],src:i[2]}),a++;continue}if(/^\s*>/.test(s)){const c=[];for(;a<t.length&&/^\s*>/.test(t[a]);)c.push(t[a++].replace(/^\s*>\s?/,""));const g=(r=c[0])==null?void 0:r.match(/^\[!(TIP|NOTE|WARNING|IMPORTANT|CAUTION)\]\s*$/i),u=g?g[1].toUpperCase()==="TIP"?"tip":["WARNING","CAUTION"].includes(g[1].toUpperCase())?"warning":"note":"quote";o.push({kind:"callout",variant:u,blocks:R((g?c.slice(1):c).join(`
`))});continue}if(/^\s*\|/.test(s)){const c=[];for(;a<t.length&&/^\s*\|/.test(t[a]);)c.push(t[a++]);const g=D(c[0]),u=c.length>1&&/^[\s|:-]+$/.test(c[1]),w=u?D(c[1]).map(m=>m.startsWith(":")&&m.endsWith(":")?"center":m.endsWith(":")?"right":void 0):[];o.push({kind:"table",header:g,align:w,rows:c.slice(u?2:1).map(D)});continue}const l=s.match(O);if(l){const c=l[1].length,g=/\d/.test(l[2]),u=[];for(;a<t.length;){const w=t[a],m=w.match(O);if(m&&m[1].length===c&&/\d/.test(m[2])===g){u.push([m[3]]),a++;continue}if(!w.trim()){let v=a+1;for(;v<t.length&&!t[v].trim();)v++;const k=t[v],S=k==null?void 0:k.match(O);if(!(k!==void 0&&(S&&S[1].length===c&&/\d/.test(S[2])===g||M(k)>c)))break;u[u.length-1].push(""),a++;continue}if(M(w)>c){u[u.length-1].push(w),a++;continue}if(!Q(w)){u[u.length-1].push(w),a++;continue}break}o.push({kind:"list",ordered:g,start:g&&parseInt(l[2],10)||1,items:u.map(w=>{const[m,...v]=w,k=Math.min(...v.filter(A=>A.trim()).map(M),1/0),S=v.map(A=>A.trim()?A.slice(Number.isFinite(k)?k:0):"");return R([m,...S].join(`
`))})});continue}const d=[];for(;a<t.length&&t[a].trim()&&(d.length===0||!Q(t[a]));)d.push(t[a++].trim());o.push({kind:"paragraph",text:d.join(" ")})}return o}const vt=new RegExp("`([^`]+)`|\\*\\*(.+?)\\*\\*|\\[([^\\]]+)\\]\\(([^)\\s]+)\\)|(?<![\\w*])\\*(?![\\s*])(.+?)\\*(?![\\w*])");function C(e){const t=[];let o=e,a=0;for(;o;){const r=o.match(vt);if(!r||r.index===void 0){t.push(o);break}r.index>0&&t.push(o.slice(0,r.index)),r[1]!==void 0?t.push(n.jsx(p,{component:"code",sx:{fontFamily:"monospace",fontSize:"0.92em",bgcolor:"action.hover",px:.5,py:.1,borderRadius:.5},children:r[1]},a++)):r[2]!==void 0?t.push(n.jsx("strong",{children:C(r[2])},a++)):r[3]!==void 0?t.push(n.jsx(Ct,{href:r[4],children:C(r[3])},a++)):t.push(n.jsx("em",{children:C(r[5])},a++)),o=o.slice(r.index+r[0].length)}return t}function Ct({href:e,children:t}){if(e.startsWith("help:")){const[o,a]=e.slice(5).split("#");return n.jsx(T,{component:b,to:{pathname:`/help/${o}`,hash:a?`#${a}`:""},children:t})}return e.startsWith("#")?n.jsx(T,{component:b,to:{hash:e},children:t}):e.startsWith("/")?n.jsx(T,{component:b,to:e,children:t}):n.jsx(T,{href:e,target:"_blank",rel:"noopener noreferrer",children:t})}function xt({alt:e,src:t}){const{t:o}=x(),[a,r]=f.useState(!1),s=/^https?:/.test(t)?t:yt(t);return n.jsxs(p,{component:"figure",sx:{mx:0,my:2.5},children:[s?n.jsx(p,{component:"img",src:s,alt:e,title:o("help.enlargeImage"),onClick:()=>r(!0),sx:{display:"block",maxWidth:"100%",maxHeight:560,borderRadius:1,border:1,borderColor:"divider",boxShadow:1,cursor:"zoom-in"}}):n.jsxs(p,{sx:{display:"flex",alignItems:"center",justifyContent:"center",gap:1,minHeight:120,border:2,borderStyle:"dashed",borderColor:"divider",borderRadius:1,color:"text.secondary",px:2},children:[n.jsx(bt,{}),n.jsx(y,{variant:"body2",children:o("help.screenshotComingSoon")})]}),e&&n.jsx(y,{component:"figcaption",variant:"caption",color:"text.secondary",sx:{display:"block",mt:.75},children:e}),s&&n.jsxs(me,{open:a,onClose:()=>r(!1),maxWidth:"xl",children:[n.jsx(ge,{"aria-label":o("common.actions.close"),onClick:()=>r(!1),sx:{position:"absolute",right:8,top:8,bgcolor:"background.paper","&:hover":{bgcolor:"background.paper"}},children:n.jsx(ye,{})}),n.jsx(we,{sx:{p:1},children:n.jsx(p,{component:"img",src:s,alt:e,sx:{display:"block",maxWidth:"100%"}})})]})]})}function Tt({variant:e,blocks:t}){const{t:o}=x();if(e==="quote")return n.jsx(p,{sx:{borderLeft:4,borderColor:"divider",pl:2,my:2,color:"text.secondary"},children:n.jsx(L,{blocks:t})});const a=e==="tip"?"success":e==="warning"?"warning":"info";return n.jsxs(B,{severity:a,icon:e==="tip"?n.jsx(kt,{fontSize:"inherit"}):void 0,sx:{my:2,"& p:last-child, & ul:last-child, & ol:last-child":{mb:0}},children:[n.jsx(fe,{children:o(`help.callout.${e}`)}),n.jsx(L,{blocks:t})]})}function St({block:e}){switch(e.kind){case"heading":{const t=e.level<=1?"h4":e.level===2?"h5":e.level===3?"h6":"subtitle1";return n.jsx(y,{id:e.id,variant:t,component:`h${Math.min(e.level,6)}`,sx:{mt:e.level<=2?4:3,mb:1.25,scrollMarginTop:72,fontWeight:e.level>=4?600:void 0},children:C(e.text)})}case"paragraph":return n.jsx(y,{variant:"body1",sx:{mb:1.5,lineHeight:1.7},children:C(e.text)});case"list":return n.jsx(p,{component:e.ordered?"ol":"ul",start:e.ordered?e.start:void 0,sx:{pl:3,mt:0,mb:1.5,"& > li":{mb:.5,lineHeight:1.7},"& li > p":{mb:.5}},children:e.items.map((t,o)=>n.jsx(y,{component:"li",variant:"body1",children:t.length===1&&t[0].kind==="paragraph"?C(t[0].text):n.jsx(L,{blocks:t})},o))});case"callout":return n.jsx(Tt,{variant:e.variant,blocks:e.blocks});case"table":return n.jsx(Fe,{component:j,variant:"outlined",sx:{my:2},children:n.jsxs(he,{size:"small",children:[n.jsx(pe,{children:n.jsx(H,{children:e.header.map((t,o)=>n.jsx(V,{align:e.align[o],sx:{fontWeight:600,whiteSpace:"nowrap"},children:C(t)},o))})}),n.jsx(ue,{children:e.rows.map((t,o)=>n.jsx(H,{children:t.map((a,r)=>n.jsx(V,{align:e.align[r],children:C(a)},r))},o))})]})});case"image":return n.jsx(xt,{alt:e.alt,src:e.src});case"hr":return n.jsx(le,{sx:{my:3}})}}function L({blocks:e}){return n.jsx(n.Fragment,{children:e.map((t,o)=>n.jsx(f.Fragment,{children:n.jsx(St,{block:t})},o))})}function At({blocks:e}){return n.jsx(p,{sx:{"& > :first-child":{mt:0}},children:n.jsx(L,{blocks:e})})}function Lt(){const{t:e,i18n:t}=x(),{topicId:o}=be(),a=f.useMemo(()=>gt(t.language),[t.language]),r=f.useMemo(()=>new Map(a.map(l=>[l.id,l])),[a]),[s,h]=f.useState("");if(!o)return n.jsx(It,{topics:a,byId:r,query:s,setQuery:h});const i=r.get(o);return n.jsxs(p,{sx:{display:"flex",gap:3,alignItems:"flex-start"},children:[n.jsx(p,{sx:{display:{xs:"none",md:"block"},width:240,flexShrink:0,position:"sticky",top:64},children:n.jsx(_t,{topics:a,byId:r,current:o,query:s,setQuery:h})}),n.jsxs(p,{sx:{flex:1,minWidth:0},children:[n.jsx(_,{component:b,to:"/help",size:"small",startIcon:n.jsx(ne,{}),sx:{display:{md:"none"},mb:1},children:e("help.allTopics")}),i?n.jsx(Ot,{topic:i,topics:a}):n.jsx(B,{severity:"warning",action:n.jsx(_,{component:b,to:"/help",children:e("help.allTopics")}),children:e("help.topicNotFound")})]})]})}function ie({query:e,setQuery:t,autoFocus:o}){const{t:a}=x();return n.jsx(Ae,{size:"small",fullWidth:!0,autoFocus:o,placeholder:a("help.searchPlaceholder"),value:e,onChange:r=>t(r.target.value),slotProps:{input:{startAdornment:n.jsx(Ie,{position:"start",children:n.jsx(_e,{fontSize:"small"})})}}})}function It({topics:e,byId:t,query:o,setQuery:a}){const{t:r}=x(),s=f.useMemo(()=>ae(e,o),[e,o]),h=o.trim().length>0;return n.jsxs(I,{spacing:3,sx:{maxWidth:1100},children:[n.jsxs(p,{children:[n.jsx(y,{variant:"h4",gutterBottom:!0,children:r("help.title")}),n.jsx(y,{color:"text.secondary",children:r("help.subtitle")})]}),n.jsx(p,{sx:{maxWidth:560},children:n.jsx(ie,{query:o,setQuery:a,autoFocus:!0})}),h?n.jsxs(I,{spacing:1.5,children:[n.jsx(y,{variant:"overline",color:"text.secondary",children:r("help.resultCount",{count:s.length})}),s.length===0&&n.jsx(B,{severity:"info",children:r("help.noResults",{query:o.trim()})}),s.map(({topic:i,snippet:l})=>n.jsx(ke,{variant:"outlined",children:n.jsx(Pe,{component:b,to:`/help/${i.id}`,children:n.jsxs(ve,{children:[n.jsx(y,{variant:"h6",children:i.title}),n.jsx(y,{variant:"body2",color:"text.secondary",children:l})]})})},i.id))]}):n.jsxs(n.Fragment,{children:[t.has("getting-started")&&n.jsx(B,{severity:"info",action:n.jsx(_,{component:b,to:"/help/getting-started",endIcon:n.jsx(te,{}),children:r("help.startHereAction")}),children:r("help.startHere")}),n.jsx(p,{sx:{display:"grid",gap:2,gridTemplateColumns:{xs:"1fr",sm:"repeat(2, 1fr)",lg:"repeat(3, 1fr)"}},children:G.map(i=>{const l=i.topics.map(d=>t.get(d)).filter(d=>!!d);return l.length===0?null:n.jsxs(j,{variant:"outlined",sx:{p:2},children:[n.jsx(y,{variant:"overline",color:"primary",sx:{fontWeight:600},children:r(`help.groups.${i.key}`)}),n.jsx(I,{spacing:1.5,sx:{mt:.5},children:l.map(d=>n.jsxs(p,{children:[n.jsx(T,{component:b,to:`/help/${d.id}`,variant:"subtitle1",underline:"hover",sx:{fontWeight:600},children:d.title}),n.jsx(y,{variant:"body2",color:"text.secondary",children:d.summary})]},d.id))})]},i.key)})})]})]})}function _t({topics:e,byId:t,current:o,query:a,setQuery:r}){const{t:s}=x(),h=f.useMemo(()=>a.trim()?new Set(ae(e,a).map(i=>i.topic.id)):void 0,[e,a]);return n.jsxs(j,{variant:"outlined",sx:{maxHeight:"calc(100vh - 88px)",overflowY:"auto"},children:[n.jsx(p,{sx:{p:1.5,pb:.5},children:n.jsx(ie,{query:a,setQuery:r})}),n.jsxs(Ce,{dense:!0,disablePadding:!0,children:[n.jsx(z,{component:b,to:"/help",children:n.jsx(q,{primary:s("help.allTopics"),slotProps:{primary:{color:"primary",fontWeight:600}}})}),G.map(i=>{const l=i.topics.map(d=>t.get(d)).filter(d=>!!d&&(!h||h.has(d.id)));return l.length===0?null:n.jsx("li",{children:n.jsxs("ul",{style:{padding:0},children:[n.jsx(xe,{sx:{lineHeight:"32px",bgcolor:"background.paper"},children:s(`help.groups.${i.key}`)}),l.map(d=>n.jsx(z,{component:b,to:`/help/${d.id}`,selected:d.id===o,sx:{pl:3},children:n.jsx(q,{primary:d.title})},d.id))]})},i.key)}),h&&h.size===0&&n.jsx(y,{variant:"body2",color:"text.secondary",sx:{px:2,py:1},children:s("help.noResults",{query:a.trim()})})]})]})}function Ot({topic:e,topics:t}){const{t:o}=x(),a=Te(),r=Se(),s=f.useMemo(()=>R(e.body),[e]),h=s.filter(c=>c.kind==="heading"&&c.level===2),i=t.findIndex(c=>c.id===e.id),l=i>0?t[i-1]:void 0,d=i>=0&&i<t.length-1?t[i+1]:void 0;return f.useEffect(()=>{const c=decodeURIComponent(a.hash.replace(/^#/,""));if(!c){window.scrollTo({top:0});return}let g=!1;const u=()=>{var m;g||(m=document.getElementById(c))==null||m.scrollIntoView({behavior:"smooth",block:"start"})};u();const w=[...document.querySelectorAll("article img")].filter(m=>!m.complete);return w.length&&Promise.all(w.map(m=>m.decode().catch(()=>{}))).then(u),()=>{g=!0}},[a.hash,e.id]),n.jsxs(p,{sx:{display:"flex",gap:4,alignItems:"flex-start"},children:[n.jsxs(p,{component:"article",sx:{flex:1,minWidth:0,maxWidth:880},children:[n.jsx(y,{variant:"h4",component:"h1",gutterBottom:!0,children:e.title}),n.jsx(At,{blocks:s}),n.jsxs(I,{direction:"row",spacing:2,sx:{mt:5,pt:2,borderTop:1,borderColor:"divider"},justifyContent:"space-between",children:[l?n.jsx(_,{startIcon:n.jsx(ne,{}),onClick:()=>r(`/help/${l.id}`),sx:{textAlign:"left"},children:n.jsxs(p,{children:[n.jsx(y,{variant:"caption",display:"block",color:"text.secondary",children:o("help.previous")}),l.title]})}):n.jsx("span",{}),d&&n.jsx(_,{endIcon:n.jsx(te,{}),onClick:()=>r(`/help/${d.id}`),sx:{textAlign:"right"},children:n.jsxs(p,{children:[n.jsx(y,{variant:"caption",display:"block",color:"text.secondary",children:o("help.next")}),d.title]})})]})]}),h.length>1&&n.jsxs(p,{component:"nav",sx:{display:{xs:"none",lg:"block"},width:220,flexShrink:0,position:"sticky",top:64},children:[n.jsx(y,{variant:"overline",color:"text.secondary",children:o("help.onThisPage")}),n.jsx(I,{spacing:.75,sx:{mt:.5,borderLeft:2,borderColor:"divider",pl:1.5},children:h.map(c=>n.jsx(T,{component:b,to:{hash:`#${c.id}`},variant:"body2",underline:"hover",color:a.hash===`#${c.id}`?"primary":"text.secondary",children:c.text.replace(/\*\*|`/g,"")},c.id))})]})]})}export{Lt as HelpPage};
