# Tag rules

Tag rules tag cards for you. Each rule is a search for one game, such as `t:creature r>=rare` or `is:commander`, plus one or more tags. Cards that match get those tags when you scan or import them, and an administrator can apply a rule to the cards you already own.

## How tag rules work

- **Rules only add tags.** They never remove a tag, even if a card stops matching. If you change a rule, fix any cards that were tagged under the old rule yourself.
- **Each rule is for one game.** It can use every search field that game has. See [Search syntax](help:search-syntax).
- **Rules look at the card itself.** They can use the printing (name, set, type, rarity, colors, rules text and so on) and your copy's foil, condition and language. They can't use location (`loc:`), tags (`tag:`), prices (`usd`, `price` and so on) or `date:`. A scanned card's location isn't known until it's added, and prices change after a tag is applied.
- **Only administrators** can create, change or run rules. Everyone's scans and imports get the tags.

## Create a rule

1. Go to **Administration ▸ Tag Rules**.
2. Choose a **Game**, then click **Add rule**.
3. Enter a **Name**, for example *Rares for the binder*.
4. Under **Cards that match**, type a search, the same way you would on the [Collection](/collection) page. Click the **?** icon for the game's fields.
5. Under **Tags to add**, pick existing tags or type new ones and press **Enter**. A new tag is created the first time the rule tags a card.
6. Leave **Apply to new scans and imports** on. Turn it off to keep a rule without using it.
7. Check **Cards you already own**. It shows how many of your cards match and how many would get new tags, with some examples.
8. Click **Save**.

If the search has a problem, such as a misspelled field or a field rules can't use, a red message explains it and **Save** stays greyed out.

> [!TIP]
> A misspelled field is an error in a rule, not a name search as it is in the search box. That stops a typo from tagging the wrong cards.

## Tag the cards you already own

New rules only tag new cards. To tag the cards already in your collection:

1. On the **Tag Rules** tab, click **Run now** (the play button) on the rule's row.
2. Check the number of cards that will be tagged and the examples.
3. Click **Run now**.

Cards that already have every tag are skipped, so it's safe to run a rule again. **Run now** works even when the rule is turned off, and it covers every site.

## Tags from rules when you scan

On the [Scan](help:scanning) page, matching cards get their rule tags while you review them. A tag added by a rule has a sparkle icon, on the row and in the card's **Tags** box.

- **Remove a rule tag** in the card's **Tags** box if you don't want it. The rule won't add it back to that card.
- **Rule tags update when the card changes.** If you correct the match, change foil, condition or language, or switch game, the card is checked again.
- **Turn off Apply tag rules** in the action bar to take all rule tags off and stop adding them. Your browser remembers the choice.

The **Add N confirmed cards** button waits a moment until every card has its rule tags. The same applies to [scan batches](help:scan-batches) and [location audits](help:location-audit).

## Tags from rules when you import

These imports apply your rules as the cards are added:

- **Import a CSV file** and **Import a deck from Moxfield or Archidekt** on the [Import](help:importing) page
- **Import** on a location's page
- **Add new** or **Move & add** on a list (new cards only)
- **Add card** on a location's page, and **Add card to this pocket…** in a binder when you pick a card from the catalog

Where the import shows a result message, it says how many new cards the rules tagged. Imports have no review step, so rule tags can't be removed before the cards are added. Remove them afterwards on the [Collection](help:collection) page if you need to.

## Edit, turn off or delete a rule

- Click the pencil on a rule's row to edit it.
- Use the **Enabled** switch to turn a rule off or on.
- Click the bin to delete a rule. Tags it already added stay on the cards.

## Related topics

- [Search syntax](help:search-syntax): the fields you can use in a rule
- [Scanning cards](help:scanning): reviewing scans and their tags
- [Administration](help:administration): the other Administration tabs
