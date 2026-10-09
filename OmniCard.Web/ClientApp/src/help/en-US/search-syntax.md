# Search syntax

OmniCard's search boxes understand a Scryfall-style search language: plain words for card names, plus fields like `t:creature`, `set:dom` or `tag:trade` that you can combine with `or`, `-` and parentheses. This topic lists every field and operator for each game.

## Where you can use it

The same language works in two kinds of search box:

- **Collection search** looks through the cards you own. You'll find it on the [Collection](/collection) page, on a location's page, and in a binder's Unplaced pool. On the Collection and location pages, press **Enter** to run the search. See [Collection](help:collection).
- **Card search** looks through the full card catalog for a game, including cards you don't own. It's used when you correct a scan match and when you add a card to a location, binder pocket or list. See [Scanning](help:scanning).

Most fields work in both. Fields about your own copies (tags, condition, location) only work in collection search, and the `order:` and `unique:` directives only work in card search. The tables below say which.

> [!TIP]
> Click the **Search syntax help** icon (**?**) at the right of a collection search box to see the fields for the game selected in the top bar, each with an example you can copy.

![The search syntax help popover listing fields and examples](search-syntax-help-popover.png)

## The basics

| You type | What it finds |
|---|---|
| `bolt` | Cards whose name contains "bolt". |
| `lightning bolt` | Names containing both "lightning" and "bolt". |
| `"lightning bolt"` | Names containing the exact phrase. |
| `!"Lightning Bolt"` | Cards named exactly Lightning Bolt. |
| `t:dragon` | Cards whose type contains "dragon". |
| `t:dragon c:r` | Both must match (a space means AND). |
| `t:dragon or t:angel` | Either one can match. |
| `-is:foil` | Excludes matching cards. |
| `(t:goblin or t:elf) c:g` | Parentheses group terms. |

Things to know:

- Searches ignore upper and lower case.
- Put quotes around a value that contains spaces, for example `t:"legendary creature"` or `loc:"red binder"`.
- `or` can be typed as `or` or `OR`.
- A field name the game doesn't recognize is treated as a name search.

## Operators

Put an operator between the field name and the value, with no spaces: `cmc>=3`.

| Operator | Meaning | Example |
|---|---|---|
| `:` | Contains, or "has" for colors and flags | `t:elf` |
| `=` | Exactly equals | `cond=nm` |
| `!=` | Doesn't equal | `set!=dom` |
| `<` | Less than | `cmc<3` |
| `>` | Greater than | `hp>100` |
| `<=` | Less than or equal | `level<=4` |
| `>=` | Greater than or equal | `r>=rare` |
| `-` before a term | Not | `-tag:trade` |
| `not:` | Same as `-is:` | `not:foil` |

The comparison operators (`<`, `>`, `<=`, `>=`) only work on fields that hold numbers or ordered values. The tables below mark these fields with "supports < >".

## Fields for every game

These fields work in collection search for every game.

| Field | Short forms | Example | What it matches |
|---|---|---|---|
| name | `n` | `name:bolt` | Card name (bare words do the same). |
| set | `s`, `e`, `edition` | `set:dom` | Set code, exactly. |
| cn | `number` | `cn:123` | Collector number, exactly. |
| type | `t` | `t:creature` | Words in the card's type. |
| rarity | `r` | `r:rare` | Rarity. Supports < > for Magic rarities. |
| color | `c`, `id`, `ci`, `identity`, `commander` | `c:wu` | Magic colors (see below). |
| condition | `cond` | `cond:nm` | Your copy's condition. |
| lang | `language` | `lang:ja` | Your copy's language. |
| location | `loc` | `loc:binder` | The name of the location the card is in. |
| tag | `tags` | `tag:trade` | One of your tags on the card. |
| is | `not` | `is:foil` | Flags on your copy (see below). |
| foil | | `foil:true` | Foil (`true`) or non-foil (`false`). |

### Details

- **Rarity order.** For Magic, `r>=rare` finds rares and mythics, and `r<rare` finds commons and uncommons. The order is common, uncommon, rare, mythic. For other games, use the rarity name, for example `r:"super rare"`.
- **Language.** Use a code such as `en`, `ja`, `de`, `fr`, `it`, `es`, `pt`, `ko`, `ru`, `zhs` or `zht`. Many spellings also work, such as `lang:jp` or `lang:japanese`. Cards with no language set count as English.
- **Tags.** `tag:foo` matches any tag containing "foo". `tag=foo` matches the tag "foo" exactly. `-tag:foo` finds cards without it.
- **Location.** `loc:binder` matches every location with "binder" in its name. Use `loc="Red Binder"` for one exact location.

### is: flags

| Flag | Finds |
|---|---|
| `is:foil` | Foil copies. |
| `is:nonfoil` | Non-foil copies. |
| `is:missing` | Copies flagged as missing, for example by an audit. |
| `is:missingdb` | Copies flagged because the card couldn't be found in the card catalog. |

Use `-is:foil` or `not:foil` for the opposite. For Magic, collection search also understands the printing flags listed under [is: flags in card search](help:search-syntax#is-flags-in-card-search), such as `is:commander` or `is:reprint`. An `is:` flag OmniCard doesn't know finds nothing.

### Magic colors

Colors use the letters W (white), U (blue), B (black), R (red) and G (green), or the words `white`, `blue`, `black`, `red`, `green`.

| You type | Finds |
|---|---|
| `c:r` | Cards that include red. |
| `c:wu` or `c>=wu` | Cards that include white and blue (and maybe more). |
| `c=wu` | Exactly white and blue. |
| `c<=wu` | Only white, blue, or both. Nothing else. |
| `c!=wu` | Anything except exactly white and blue. |
| `c:colorless` or `c:c` | Colorless cards and lands. |
| `c:multicolor` or `c:multi` | Cards with two or more colors. |

In collection search, `id:` behaves the same as `c:`. Card search treats them separately (see below).

> [!NOTE]
> The **price** and **date** fields appear in the help list but don't filter your collection yet. To find your most valuable cards, sort the list by the **Market** column instead.

## Magic: The Gathering

### Extra fields in collection search

When Magic is selected in the top bar, collection search also understands these fields.

| Field | Short forms | Example | What it matches |
|---|---|---|---|
| oracle | `o` | `o:"draw a card"` | Words in the rules text. |
| fulloracle | `fo` | `fo:trample` | Rules text including reminder text. |
| flavor | `ft` | `ft:goblin` | Words in the flavor text. |
| artist | `a` | `a:"rebecca guay"` | Illustrator name. |
| watermark | `wm` | `wm:azorius` | Watermark. |
| cmc | `mv`, `manavalue` | `cmc>=7` | Mana value. Supports < >. |

For `cmc`, use `-cmc:3` rather than `cmc!=3`.

Collection search for Magic also understands the card search fields below, such as `pow>=5`, `kw:flying`, `f:modern` or `usd<1`. They describe the printing you own. A few keep their collection meaning: `c` and `id` use the **Magic colors** rules above, `lang` is your copy's language, and `date` isn't available (use `year` for the printing's release year).

### Card search (full Scryfall syntax)

When you look up a Magic card to add or to correct a scan, the search supports nearly all of [Scryfall's syntax](https://scryfall.com/docs/syntax).

| Field | Short forms | Example | What it matches |
|---|---|---|---|
| name | `n` | `n:bolt` | Card name. |
| set | `s`, `e`, `edition` | `s:dom` | Set code, or words in the set name. `set=` matches the code only. |
| block | | `block:innistrad` | Words in the set name. |
| st | `settype` | `st:masters` | Set type (expansion, masters, commander, …). |
| cn | `number` | `cn>=300` | Collector number. Supports < >. |
| type | `t` | `t:"legendary creature"` | Type line. |
| oracle | `o` | `o:"~ deals 3"` | Rules text. `~` stands for the card's own name. |
| fulloracle | `fo` | `fo:trample` | Rules text including reminder text. |
| keyword | `kw` | `kw:flying` | Keyword ability. |
| mana | `m`, `manacost` | `m:{2}{W}{W}` | Mana cost. `m:2ww` also works. `=` means exactly. |
| cmc | `mv`, `manavalue` | `mv<=2` | Mana value. Supports < >. |
| power | `pow` | `pow>=5` | Power. Supports < >, and `pow>tou`. |
| toughness | `tou` | `tou<3` | Toughness. Supports < >. |
| loyalty | `loy` | `loy>=5` | Starting loyalty. Supports < >. |
| defense | `def` | `def>=4` | Battle defense. Supports < >. |
| pt | `powtou` | `pt:2/2` | Power and toughness together. |
| colors | `c`, `color` | `c:rg` | Card colors. |
| identity | `id`, `ci`, `commander` | `id<=wu` | Color identity, for Commander. |
| produces | | `produces:g` | Colors of mana the card can make. |
| devotion | | `devotion>=3` | Number of colored mana symbols. Supports < >. |
| rarity | `r` | `r>=rare` | Rarity. Supports < >. |
| artist | `a` | `a:"rebecca guay"` | Illustrator. |
| flavor | `ft` | `ft:goblin` | Flavor text. |
| watermark | `wm` | `wm:azorius` | Watermark. |
| has | | `has:watermark` | `watermark`, `indicator` or `flavor`. |
| border | | `border:borderless` | Black, white, silver or borderless. |
| frame | | `frame:showcase` | Frame year (`2015`) or effect (`showcase`, `extendedart`). |
| stamp | | `stamp:acorn` | Security stamp. |
| layout | | `layout:transform` | Card layout. |
| game | | `game:arena` | Where it's available: paper, mtgo or arena. |
| in | | `in:paper` | Same as `game:`. |
| lang | `language` | `lang:ja` | Printing language. |
| year | | `year>=2020` | Release year. Supports < >. |
| date | | `date>=2024-01-01` | Release date. Supports < >. |
| usd | | `usd<1` | US dollar price. Supports < >. |
| eur | | `eur<1` | Euro price. Supports < >. |
| tix | | `tix<5` | MTGO ticket price. Supports < >. |
| edhrec | | `edhrec<1000` | EDHREC popularity rank (lower is more popular). |
| format | `f`, `legal` | `f:modern` | Legal or restricted in a format. |
| banned | | `banned:legacy` | Banned in a format. |
| restricted | | `restricted:vintage` | Restricted in a format. |

Colors in card search work like the **Magic colors** table above, and also accept `m` for multicolor and a number for how many colors a card has, for example `c>=2`.

#### is: flags in card search

In card search, `is:` describes the printing, not your copy.

| Group | Flags |
|---|---|
| Finish | `is:foil`, `is:nonfoil`, `is:etched`, `is:glossy` |
| Printing | `is:promo`, `is:reprint`, `is:firstprint`, `is:reserved`, `is:digital`, `is:booster`, `is:oversized`, `is:variation`, `is:hires` |
| Art | `is:fullart`, `is:textless`, `is:spotlight` |
| Mana | `is:colorless`, `is:multicolor` (or `is:gold`), `is:hybrid`, `is:phyrexian` |
| Layout | `is:split`, `is:flip`, `is:transform`, `is:meld`, `is:leveler`, `is:dfc`, `is:mdfc`, `is:adventure`, `is:token` |
| Card kind | `is:permanent`, `is:spell`, `is:land`, `is:creature`, `is:vanilla`, `is:commander` |
| Other | `is:gamechanger`, `is:contentwarning`, `is:funny` |

#### Ordering card search results

Add these anywhere in a card search to change the result order. They don't filter anything.

| Directive | Values |
|---|---|
| `order:` | `name`, `cmc`, `power`, `toughness`, `loyalty`, `released`, `rarity`, `color`, `usd`, `eur`, `tix`, `edhrec`, `set`, `artist`, `cn` |
| `direction:` | `asc` or `desc` |
| `unique:` | `cards` (one result per card name), `art` (one per artwork) or `prints` (every printing, the default) |

Example: `t:dragon order:usd direction:desc` lists the priciest dragons first.

> [!NOTE]
> `order:` only works in card search. To sort your collection, click a column header on the Collection page.

## One Piece Card Game

| Field | Short forms | Example | What it matches |
|---|---|---|---|
| cost | | `cost:4` | Play cost. |
| power | `pow` | `power:5000` | Power. |
| counter | `ctr` | `counter>=1000` | Counter value. Supports < >. |
| life | | `life:5` | Leader life. |
| attribute | `attr` | `attribute:slash` | Attribute (Slash, Strike, …). |
| subtype | `sub`, `trait` | `subtype:straw` | Subtype or trait. |

`cost` and `power` match the value you type. Only `counter` supports < >. In card search, `color:red` also finds cards by color, and `set:` matches the set code or set name.

## Riftbound

| Field | Short forms | Example | What it matches |
|---|---|---|---|
| domain | `d` | `domain:body` | Any of a card's domains. |
| energy | | `energy>=4` | Energy cost. Supports < >. |
| might | `m` | `might>=5` | Might. Supports < >. |
| power | `pow` | `power>=3` | Power. Supports < >. |
| supertype | `super` | `supertype:champion` | Supertype. |

In card search, `set:` matches the set code or set name, and `cn:` supports < >.

## Pokémon

| Field | Short forms | Example | What it matches |
|---|---|---|---|
| hp | | `hp>=200` | Hit points. Supports < >. |
| stage | | `stage:basic` | Evolution stage. |

## Yu-Gi-Oh!

| Field | Short forms | Example | What it matches |
|---|---|---|---|
| attribute | `attr` | `attribute:dark` | Monster attribute (DARK, LIGHT, …). |
| level | `lvl`, `rank` | `level>=8` | Level or Rank. Supports < >. |
| atk | | `atk>=3000` | ATK. Supports < >. |
| def | | `def>=2500` | DEF. Supports < >. |

## Final Fantasy TCG

| Field | Short forms | Example | What it matches |
|---|---|---|---|
| element | `e`, `el` | `element:fire` | Element. |
| cost | | `cost>=5` | Casting cost. Supports < >. |
| power | `pow` | `power>=8000` | Power. Supports < >. |
| job | | `job:warrior` | Job. |
| category | `cat` | `category:vii` | Category, for example VII or XIV. |

Element shorthands: `f` Fire, `i` Ice, `l` Lightning, `w` Water, `wi` Wind, `ea` Earth, `li` Light, `d` Dark. So `e:f` finds Fire cards.

> [!WARNING]
> In Final Fantasy TCG, `e:` means element, not set. Use `s:` or `set:` to search by set.

For Pokémon, Yu-Gi-Oh! and Final Fantasy TCG, `<` and `>` compare numbers when both sides are numbers. In card search, `set:` matches the set code or set name.

## Searching with All Games selected

When **All Games** is selected in the top bar, collection search still works across every game, with two differences:

- Game fields only work by their full name, such as `artist:`, `cmc>=3`, `element:fire` or `hp>=100`. Short forms that belong to one game (like `a:` or `mv`) are treated as a name search.
- A field several games share, such as `power`, matches cards from each game that has it.

Select a single game for the full set of short forms.

## Example searches

| Search | Finds |
|---|---|
| `t:creature c=g cmc<=2` | Mono-green creatures with mana value 2 or less (Magic). |
| `r>=rare -is:foil loc:bulk` | Non-foil rares and mythics in locations named "bulk". |
| `tag:trade or tag:sell` | Cards tagged trade or sell. |
| `lang:ja is:foil` | Japanese foil copies. |
| `set:dom -cond:nm` | Dominaria cards that aren't Near Mint. |
| `o:"draw a card" t:instant` | Instants that draw a card (Magic). |
| `e:f cost>=5` | Fire cards costing 5 or more (Final Fantasy TCG). |
| `hp>=200 stage:basic` | Basic Pokémon with 200+ HP. |
| `level>=8 attr:dark` | Level 8+ DARK monsters (Yu-Gi-Oh!). |
| `might>=5 domain:body` | Body cards with 5+ might (Riftbound). |
| `t:dragon order:usd direction:desc` | Dragons, most expensive first (Magic card search). |

## Troubleshooting

- **No results.** Check that the right game is selected in the top bar, and that values with spaces are in quotes.
- **A field seems to be ignored.** It may not apply where you're searching. For example, `tag:` only works in collection search, and `order:` only works in card search.
- **`set:` finds nothing.** In collection search, `set:` needs the set code, such as `set:dom`, not the set name.
- **Numbers compare oddly.** Only fields marked "supports < >" compare by value. Others match the text you type.
