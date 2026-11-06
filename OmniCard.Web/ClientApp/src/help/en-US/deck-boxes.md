# Deck boxes

A deck box is a location that holds one built deck. It belongs to a single game, can be given a deck type (format) such as Commander or Standard, and warns you when the deck doesn't meet that format's rules. A Magic deck box can also be linked to its deck on Moxfield or Archidekt and synced from it.

## What makes a deck box different

Deck boxes are ordinary [locations](help:locations) with a few extras:

- **One game only.** Every deck box is assigned a game, and cards from other games can't be put in it.
- **Deck type.** You can pick a format for the deck. OmniCard then checks the deck's size, copy limits and commander against that format.
- **Deck panel.** The deck box's page shows the game, deck type, card count and any legality warnings.
- **Stacked view.** The deck can be shown as overlapping card stacks grouped by type, like a deck-building site.
- **Deck link.** A Magic deck box can be linked to its deck on Moxfield or Archidekt, then kept up to date with **Sync from URL**.
- **Where each card came from.** OmniCard remembers where every card was before it went into a deck box, so you can [deconstruct the deck](#deconstruct-a-deck) and put each card back where it was.

## Create a deck box

1. Go to [Locations](/locations).
2. Type a name in **New location name**.
3. Set **Type** to **Deck Box**.
4. Choose a **Game**. This is required for a deck box.
5. Optionally choose a **Deck type**, or leave it as **None**. The list only shows formats for the game you picked.
6. Click **Add**.

![Creating a deck box with a game and deck type](deck-boxes-add-bar.png)

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

- **Add card** on the deck box's page is locked to the deck box's game (*Locked to this location's game*).
- In "move to location" pickers, deck boxes for a different game are greyed out, with the note *This location only holds … cards*.
- Any other attempt to move a card from another game into the deck box is refused.

The game selector at the top of the app also hides deck boxes that belong to other games on the Locations page. Choose **All Games** to see them all.

Binders and boxes can be kept to one game in the same way, and any location can have default sets for its searches. See [Game and default sets](help:locations#game-and-default-sets).

## The deck panel

Open a deck box from the Locations page to see its panel above the card list.

![A deck box page with its deck panel and legality warnings](deck-boxes-panel.png)

The panel shows:

- The game. If none is assigned, you'll see **No game assigned** instead.
- The deck type, if one is set.
- How many commanders the deck has, when it has any.
- The total card count, for example *100 cards (99 + commander)*.
- The **Game & deck type** button.
- **Deconstruct deck**, when the deck box holds cards. See [Deconstruct a deck](#deconstruct-a-deck).
- **Put-back reports**, once cards have been returned from the deck box.
- Any legality warnings, under **Deck legality (*deck type*)**.
- The deck link, if the deck box has one. See [Link a deck box to an online deck](#link-a-deck-box-to-an-online-deck).

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

- `commander`: the card is a commander, leader or other command-zone card. Commanders count toward the deck size, are shown in their own **Commander** group in the stacked view, and satisfy the "needs a commander" rule. A deck can have more than one (for example partners).
- `sideboard`: the card is in the sideboard. Sideboard cards are left out of the deck-size and copy-limit checks.

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

- **Type**: a **Commander** group first (cards tagged `commander`), then one group per card type. Magic decks use Creature, Planeswalker, Battle, Instant, Sorcery, Artifact, Enchantment and Land, in that order. Other games use their own main types, such as Pokémon, Trainer and Energy, or Monster, Spell and Trap. A card with several types goes in its main group; an Artifact Creature is grouped with Creatures.
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

## Link a deck box to an online deck

You can link a deck box to its deck on Moxfield or Archidekt. When you change the deck online, **Sync from URL** brings the deck box up to date with cards from your collection. Moxfield and Archidekt are Magic: The Gathering sites, so only Magic deck boxes can be linked. The deck must be public.

### Link or change the link

- **Import links automatically.** When you import a deck URL into a deck box that has no link yet, the deck box is linked to that deck. This works from **Import** on the deck box's page and from **Import from Moxfield / Archidekt** on the [Import / Export](/import) page. A deck box that's already linked keeps its link.
- **Link by hand.** In the deck panel, click **Link to deck URL**, paste the deck's address into **Deck URL**, and click **Save**.
- **Change or remove the link.** Click **Edit link** in the deck panel. Paste a new address and click **Save**, or click **Remove link**. Either one also clears the deck box's "not in collection" list.

Once linked, the deck panel shows **Linked to Moxfield** (or **Archidekt**), which opens the deck online, and when the deck box was last synced. On the [Locations](/locations) page, a link icon next to the deck box's name shows it's linked. Click the icon to open the deck in a new tab.

### Sync from URL

1. Open the deck box and click **Sync from URL** in the deck panel. OmniCard downloads the deck and compares it with the cards in the deck box. Nothing changes until you apply.
2. Review **Cards to add**. These are cards the deck needs more copies of. Cards are matched by name, so any printing in the deck box counts. For each card, OmniCard lists copies it found elsewhere in your collection and fills in how many to use. The deck's exact printing comes first, marked **Exact printing**. Change the amounts in **Use** if you want different copies.
3. Deal with every card that is still short:
   - Click **Find a copy…** to search your collection by name and choose a copy with **Use**. Use this when OmniCard didn't find the card, for example when your copy is spelled differently.
   - Or tick **Not in collection** for the card. **Mark the rest as not in collection** ticks every card that's still short.
4. Review **Cards to remove**. These are cards in the deck box that the deck no longer has. **Came from** shows where each one was before it went into a deck. For each one choose:
   - **Return where it came from** puts it back in that location and binder pocket. This is the default when OmniCard knows where the card came from. See [Deconstruct a deck](#deconstruct-a-deck) for what happens when it can't go back.
   - **Move out** moves it to the location in **Move removed cards to**, which starts at Bulk.
   - **Keep as sideboard** leaves it in the deck box and tags it `sideboard`.
   - **Leave in box** leaves it alone.

   When only **Return where it came from** is chosen, the location box is called **Cards with no known origin go to**.
5. Click **Apply sync**. If any cards were returned, click **Download put-back list (PDF)** to print where they go.

A few things to know:

- Copies listed for sale are shown as **Listed for sale** and can't be used. Unlist them first.
- Copies in locations ignored for lists are marked **Ignored location**. They're never filled in for you, but you can still use them.
- Only copies in sites you can change are offered, because using a copy moves it into the deck box.
- Cards already tagged `sideboard` are set to **Leave in box** by default, so they aren't moved out on every sync.

### Cards not in your collection

The cards you marked **Not in collection** are remembered. The deck panel shows a chip such as *3 cards not in collection*. Click it to see the list, or click **×** next to a card to remove it from the list.

On the next sync these cards are checked again. A card you've since added to your collection is filled in like any other. A card you still don't own stays ticked as **Not in collection**. Each sync replaces the list with the cards you marked that time.

> [!TIP]
> To make a shopping list for the missing cards, import the same deck URL into [Lists](help:lists).

## Deconstruct a deck

OmniCard remembers where each card was before it went into a deck box: the location, the binder page and slot, and the section. It does this for every way of moving a card into a deck box, such as **Move to**, **Move to deck box…**, **Sync from URL**, list **Fulfill** and editing a card's location. Moving a card from one deck box to another keeps the place it came from before the first deck. Moving it anywhere other than a deck box forgets it.

When you're done with a deck, you can send every card back where it came from:

1. Open the deck box and click **Deconstruct deck** in the deck panel. OmniCard shows where every card will go, grouped by location and listed by page and slot. Nothing changes yet.
2. Check **Cards with no known origin go to**. It starts at Bulk.
3. Leave **Save the deck as a card list first, so it can be rebuilt** ticked to keep the decklist in [Lists](help:lists) under the name in **List name**. To rebuild the deck later, use the list's **Fulfill** to move the cards back into the deck box.
4. Click **Deconstruct deck**.
5. Click **Download put-back list (PDF)** to print a checklist of where every card goes, then put the cards away.

The deck box is left empty. It keeps its game, deck type and deck link, so a linked deck can also be rebuilt with **Sync from URL**.

To return only some cards, open the deck box, click **Select**, tick the cards and click **Return to where it came from**.

### When a card can't go back

The put-back list highlights each card that can't go back where it came from, with the reason:

| Reason | Where the card goes |
|---|---|
| Where it came from isn't known. The card was added straight into the deck box, or was there before OmniCard started remembering. | The location in **Cards with no known origin go to** |
| The location it came from was deleted. | Bulk |
| It came from a site you can't edit. | Bulk |
| It came from a location that now only holds another game. | Bulk |
| The binder pocket it came from now holds a different card. That card stays where it is. | Bulk |

A few things to know:

- A card that came from a stack in a box joins that stack again, as long as the stack is still there and the copies match (same condition, language, note and tags). The put-back list says **Joins the stack there**.
- The `commander` and `sideboard` tags are removed from returned cards, because they only mean something in a deck.
- A card listed for sale can be returned with all its copies, but not just some of them. Unlist it first.

### Put-back reports

Every deconstruction or return is saved. The deck panel shows a **Put-back reports** button once there's at least one. Click it to see each report, open it again, or click **Download put-back list (PDF)** to print it again.

## Troubleshooting

- **A deck box is missing from the Locations page.** The game selector is set to a different game. Choose that game or **All Games**.
- **I can't move a card into a deck box.** The card is from a different game than the deck box.
- **Saving a new game for a deck box shows an error.** The deck box already holds cards from another game. Move them out first.
- **There are no legality warnings.** Either the deck meets the format's rules, or the deck box has no deck type. Set one with **Game & deck type**.
- **The deck says it needs a commander.** Tag your commander card `commander` in its details.
- **There's no Link to deck URL button.** Only Magic: The Gathering deck boxes can be linked, and you need permission to edit locations.
- **Sync from URL says it couldn't download the deck.** Check that the link opens the deck and that the deck is public on Moxfield or Archidekt.
- **Deconstruct deck sent a card to Bulk.** The put-back list says why. See [When a card can't go back](#when-a-card-can-t-go-back).
- **A card has no known origin.** OmniCard only remembers where a card came from when it's moved into a deck box. Cards that were already in deck boxes, or were scanned or imported straight into one, go to the **Cards with no known origin go to** location.
- **Apply sync is greyed out.** A card is still short and isn't ticked **Not in collection**, a copy is used more times than it has, or no **Move removed cards to** location is chosen.
