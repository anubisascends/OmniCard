# Scan batches

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
