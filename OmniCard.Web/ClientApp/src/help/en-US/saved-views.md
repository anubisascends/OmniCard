# Saved views

A saved view remembers how you like a card list laid out: the search, the sort, rows per page, **Stack duplicates**, which columns show and in what order, how rows are grouped, and on a location page, table or stacked view and how stacks are grouped. Save as many views as you like and choose one to open by default.

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
| Row grouping | The **Group by** button. See [Group rows](help:collection#group-rows). |
| Table or stacked view | The two buttons beside the search box on a location page. |
| Stack grouping | **Group by** in stacked view. |

The page you're on, the cards you've selected, which groups are collapsed and any open card details aren't part of a view.

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
> The page address includes the view, for example `/collection?view=12`. Bookmark it to come back to that view, even if another view is your default. If the view was saved for a different game, OmniCard switches the top bar to that game.

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
