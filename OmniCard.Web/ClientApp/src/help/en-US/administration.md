# Administration

Depending on your access, the Administration page is where you manage users, roles, sites, catalog data and OmniCard's settings. To change your own email or password, use [Your account](help:account) instead. This topic explains every tab and who can see it.

## Open Administration

Click **Administration** in the sidebar. Everyone can open this page, but you only see the tabs your account allows. Administrators can also open **Account ▸ Manage users** from the top bar to jump straight to the **Users** tab.

![The Administration page with its row of tabs](administration-tabs.png)

| Tab | What it's for | Who sees it |
|---|---|---|
| **Sales** | Where picked sale cards are moved | People with the Settings view permission |
| **Receipts** | Store details and layout for printed receipts | People with the Settings view permission (only administrators can change it) |
| **Scan Badges** | Value badges on the Scan page, plus watched scan folders | People with the Settings view permission (badges and folders can only be changed by administrators) |
| **Tag Rules** | Searches that tag matching cards when they're scanned or imported | Administrators only |
| **Deck Types** | Deck formats and their rules | People with the Deck Types view permission |
| **Appearance** | Card preview size | Everyone |
| **Catalog Data** | Download card catalogs, prices and artwork | People with the Catalog Data view permission |
| **eBay** | Connect eBay and set seller details | People with the eBay view permission |
| **Roles** | Permission bundles | Administrators only |
| **Sites** | Major physical places and who can see them | Administrators only |
| **Users** | Every account: add, edit, require password resets, delete | Administrators only |
| **Components** | Software versions and licenses | Everyone |

If the page scrolls sideways on a small screen, use the arrows at either end of the tab row to see more tabs.

## Users

Only administrators see this tab. It lists every account, with each person's **Username** (and email, if they have one) and **Role**.

- **system** marks the built-in Admin account. It can't be deleted and always has full access, but you can require a password reset for it.
- **awaiting password** means the person hasn't used their setup key yet, so they can't sign in until they do.
- **custom** means the person has extra permissions given or taken away on top of their role.

![The Users tab with the list of accounts](administration-users.png)

### Add a user

You don't choose the new person's password. You give them a **setup key**, and they choose their own password the first time they sign in.

1. Click **Add user**.
2. Enter a **Username**.
3. Optionally enter an **Email (optional)**. The person can then sign in with their email instead of their username. Each email can belong to only one account.
4. A **Setup key** is filled in for you. Click **Generate** for a different one, or type your own: 6 to 64 letters or numbers, with no spaces or symbols. Capitals don't matter.
5. Choose a **Role**. If you leave it as **No role**, the new user gets the *Viewer* role, which can look at everything but change nothing.
6. Tick **Administrator (full access)** instead if this person should be able to do everything, including managing users.
7. Click **Create**.
8. Give the person their username and setup key, in person or by a message only they can read. Use the copy button next to the key to copy it.

The new account shows **awaiting password** until the person signs in with the key and chooses a password.

### Change what a user can do

1. Click the **Edit user** button (pencil) on the user's row.
2. Change or clear the **Email (optional)** if needed.
3. Tick or untick **Administrator (full access)**. Administrators skip roles and permissions entirely. The built-in Admin account is always an administrator.
4. For everyone else, choose a **Role**.
5. Under **Also allow (grant)**, tick extra permissions this person should have beyond their role.
6. Under **Never allow (deny)**, tick permissions to take away from this person even if their role includes them.
7. Click **Save**.

Changes take effect straight away. The person doesn't need to sign out and back in. Their sidebar updates the next time they move to another page.

### Require a password reset

Use this when someone has forgotten their password, or you think someone else knows it. You never see or set their new password.

1. Click the **Require password reset** button (key) on the user's row.
2. A **Setup key** is filled in for you. Click **Generate** for a different one, or type your own.
3. Click **Require reset**. The person's current password stops working straight away, and their row shows **awaiting password**.
4. Give them the setup key. The next time they sign in, they enter it and choose a new password.

> [!NOTE]
> If someone enters the wrong setup key 5 times, the key stops working. Require a password reset again to give them a new one.

> [!WARNING]
> Requiring a reset doesn't sign the person out of browsers where they're already signed in. They stay signed in there until they click **Sign out** or their session ends.

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
| Sales · Orders | View, Create, Edit, Delete, Import orders, Ship (scan labels) |
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

1. Enter the **Currency code (ISO 4217)**, for example `USD`, `EUR` or `GBP`. This picks the currency sign.
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
4. For each game you scan, enter the **Folder on the server**, for example `D:\Scans\Mtg`, and make sure **Active** is on.
5. Once a folder is entered, choose the defaults for that game's batches: **Sets (art fallback)**, **Condition**, **Card language** and **Foil**.
6. Optionally, click **Choose…** next to **Default location** to pre-select where that game's batches are added. **Clear** removes it.
7. Click **Save**.

A chip next to each folder shows its status:

| Status | Meaning |
|---|---|
| **Found** | The folder exists and OmniCard can use it |
| **Folder not found** | The path is wrong, or the server can't reach it |
| **Can't move files** | OmniCard can read the folder but can't move processed files into its `_processed` subfolder. Check the folder's permissions |

Save each batch into its own subfolder, named after the batch. When OmniCard picks up a file, it moves the original into a `_processed` subfolder.

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

## Tag Rules

Only administrators see this tab. Each rule is a search for one game plus the tags to add. Matching cards get the tags when anyone scans or imports them, and **Run now** tags the matching cards you already own. Rules only ever add tags. See [Tag rules](help:tag-rules).

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

![The eBay tab showing a Connected status, the Run seller setup and Disconnect buttons, and the seller settings](administration-ebay-connected.png)

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
- [Tag rules](help:tag-rules): tagging cards automatically
- [Locations](help:locations): creating locations inside sites
- [eBay](help:ebay) and [Sales](help:sales): selling cards
