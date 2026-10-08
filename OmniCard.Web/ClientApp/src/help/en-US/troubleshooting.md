# Troubleshooting

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

**Incorrect username or password.** Check your typing. Passwords are case-sensitive, so check Caps Lock too. You can sign in with your email instead of your username if an administrator has added one to your account. If it still fails, ask an administrator to reset your password.

**You keep getting signed out.** If you don't tick **Remember me** when you sign in, you're signed out when you close the browser. Ticking it keeps you signed in on that device for up to 30 days. Clearing your browser's cookies also signs you out.

**Not authenticated. Please sign in.** Your session ended while the page was open. Reload the page and sign in again.

**You forgot your password.** Ask an administrator to require a password reset. They'll give you a setup key. Sign in with your username or email, enter the key, and choose a new password.

**You're asked for a setup key.** Your account is new, or an administrator has required a password reset. Enter the key they gave you and choose a password. If you don't have a key, ask them for one.

**That setup key is incorrect.** Check the key with your administrator. Capitals don't matter, but every letter and number does. After 5 wrong tries the key stops working and you see *Too many incorrect setup keys*. An administrator then has to require a password reset again and give you a new key.

**You're setting up a new server.** Sign in with the built-in **Admin** account (password `admin`), then change that password straight away.

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
