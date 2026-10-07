# Deck boxes

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
2. Click the **Stacked view** button (the columns icon) to the right of the search box. Click the **Table view** button (the list icon) to switch back. OmniCard remembers your choice.

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

## Troubleshooting

- **A deck box is missing from the Locations page.** The game selector is set to a different game. Choose that game or **All Games**.
- **I can't move a card into a deck box.** The card is from a different game than the deck box.
- **Saving a new game for a deck box shows an error.** The deck box already holds cards from another game. Move them out first.
- **There are no legality warnings.** Either the deck meets the format's rules, or the deck box has no deck type. Set one with **Game & deck type**.
- **The deck says it needs a commander.** Tag your commander card `commander` in its details.
