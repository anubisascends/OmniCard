# Scan batches

Scan batches let a scanner drop images into a watched folder on the server, where OmniCard matches them in the background. You then review each batch on the Scan page and add it to a location, just like an interactive scan.

## How scan batches work

With interactive scanning you upload pictures from your browser and wait while they match (see [Scanning cards](help:scanning)). Scan batches turn that around:

1. Your scanner software saves its images into a folder that OmniCard watches. Each game has its own folder.
2. Each subfolder becomes one **batch**, named after the subfolder. Images saved directly in the game's folder (not in a subfolder) go into a batch named after the date. The batch shows up on the [Scan](/scan) page as **Waiting** straight away.
3. While a batch is waiting, OmniCard leaves its files alone. It only looks at their names and sizes, so your scanner software can keep saving, renaming and finishing its run without errors.
4. The batch **starts** when someone clicks **Run**, or by itself once its files have stopped changing for a while (the *quiet period*). Each batch has its own countdown, so several can wait and start at the same time. (If an administrator has turned off automatic processing, batches only start with **Run**. See [Run batches yourself](#run-batches-yourself).)
5. When a batch starts, OmniCard takes its files in and matches the cards in the background. You don't need to have the browser open.
6. Someone opens the batch, reviews the matches, and adds the cards to a location.

Unlike interactive scans, which live only in your browser, batches are stored on the server. Anyone with access to Scan can see them, and you can stop reviewing and pick up again later, even from a different device.

When a batch starts, its original files are moved into a **_processed** subfolder of the game's folder, so they are never imported twice. An administrator can choose to have them deleted once the batch is committed or discarded.

OmniCard also skips images it has already picked up. See [Duplicate images are skipped](#duplicate-images-are-skipped).

## Set up watched folders (administrators)

An administrator sets this up once in [Administration ▸ Scan Badges](/settings?tab=scan), in the **Watched scan folders** section:

- **Watch folders** turns the feature on or off.
- **Process batches automatically** decides whether a batch starts on its own once its files have gone the quiet period without changing. Turn it off to start each batch yourself with **Run**.
- **Delete originals when a batch is closed** deletes a batch's original files from **_processed** when it is committed or discarded. When it's off, the originals are kept there.
- **Quiet period (seconds)** is how long a batch's files must go unchanged (no new, renamed or growing files) before it starts on its own. Each batch counts down separately.
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
| **Waiting** | The files are still in the scan folder, untouched. The batch starts when its countdown runs out, or when someone clicks **Run now**. Any change to its files starts the countdown again. |
| **Waiting to run** | Automatic processing is off. The files stay in the scan folder until someone clicks **Run**. |
| **Starting** | **Run** was clicked or the countdown ran out. OmniCard is taking the files in. |
| **Matching N of M** | Cards are being matched. A progress bar shows how far along it is. |
| **Ready** | Every card has been matched and the batch is ready to review. |
| **Committed** | The batch was added to your collection. |
| **Discarded** | The batch was thrown away. |

- For a waiting batch: how many files are in the folder, *Starts in m:ss* until it starts on its own, and when its files last changed. A waiting batch can't be opened yet, because nothing has been taken in.
- Once it has started: how many cards it holds, how many errors and how many committed cards it has, how many duplicate images were skipped, and when its last file arrived.
- A **Run** button while the batch is waiting (**Run now** if automatic processing is on, to start it without waiting for the countdown).
- Who is reviewing it: *You're reviewing this* or *In review by* someone else.

![The Scan batches panel with one Ready batch and one batch still matching](scan-batches-panel.png)

## Review a batch

1. In the **Scan batches** panel, click **Open** on the batch. (If you were already reviewing it, the button reads **Continue**.)
2. The batch opens on its own page, titled *Batch: name*. You are now its reviewer, and nobody else can change it while you have it.
3. Review the cards exactly as you would on the Scan page: compare the scan with the matched art, **Confirm match** or **Search catalog** to correct it, set condition, foil, tags and other properties, and use the filters and bulk **Edit**. See [Review the list](help:scanning#review-the-list) and [Correct a wrong match](help:scanning#correct-a-wrong-match). To go through a large batch quickly, press **Enter** to confirm each card and move to the next one (see [Review with the keyboard](help:scanning#review-with-the-keyboard)).
4. Check that **Add to location…** shows the right location. If the folder has a default location it is already selected; you can change it.
5. Click **Add N confirmed cards**.

The bar at the top of the batch shows the game and the batch's fixed settings (*Sets*, *Condition*, *Language*, *Foil*) taken from the folder. You can't add more pictures to a batch from the browser; save more files into the folder instead.

Your changes are saved to the server as you work. You don't have to finish in one sitting: leave the page and come back later with **Continue**.

![A batch open for review: the batch settings bar, the Validation rule box with set:fdn and 1 failed, and the review list with Birds of Paradise flagged](scan-batches-review.webp)

### Check the batch with a validation rule

To make sure every card in the batch belongs there, type a **Validation rule** in the box above the list (or load a saved one) and click **Validate** once matching has finished. Cards that don't match get a red frame and a **Failed validation** chip, and the **N failed** button shows only those cards. In the picture above, the rule `set:fdn` flags the one card that isn't from Foundations. See [Validate the list](help:scanning#validate-the-list).

### Adding cards while matching is still running

You can add confirmed cards from a batch even while the rest is still matching; new matches appear in the list as they finish. Files saved into the folder after the batch has started form a new batch, for example *Box 12 (2)*.

### Fixing errors

If some cards couldn't be matched because of an error, a **Retry N errors** button appears in the batch's settings bar. Click it to match those cards again.

### Removing cards

Click **Remove** on a card to take it out of the batch. To remove several at once, check them and click **Delete** in the list header, then confirm. Their stored images are deleted from the server. The original files stay in the **_processed** subfolder (unless **Delete originals when a batch is closed** is on, in which case they're deleted when the batch closes).

### When the batch is finished

Once every card in a batch has been added or removed, the batch closes and you return to the Scan page. It shows as **Committed** (or **Discarded** if nothing was added) in the panel for a day, then disappears from the list.

## Run batches yourself

By default a batch starts on its own once its files stop changing. If an administrator turns off **Process batches automatically**, each batch waits with the status **Waiting to run**, and its files stay untouched in the scan folder for as long as it waits. Let your scanner software finish its run, then click **Run** in the **Scan batches** panel.

When several batches are waiting, the panel header shows **Run N batches** to start them all at once.

> [!TIP]
> To leave scans out of a batch before it starts, delete or move them out of the folder. To drop a batch that hasn't started, remove its files from the folder and it disappears from the list.

## Duplicate images are skipped

Sometimes scanner software saves the same run of images twice, for example *0001.png* to *1000.png* followed by the same 1,000 files again. A scanner can't produce the exact same file twice for two passes of a real card, so OmniCard treats any file that is byte-for-byte identical to one it has already picked up for that game as a duplicate.

Duplicates are never matched and never added. The original file is still moved to **_processed**, so it isn't picked up again. The batch shows how many were skipped, and the batch page lists the repeated run, for example *0001.png – 1000.png repeat 0001.png – 1000.png (1,000 images)*.

If a batch holds nothing but duplicates (for example because the originals were already added), it closes on its own as **Discarded** as soon as it starts, and shows the skipped count in the panel for a day.

> [!NOTE]
> Only exact copies are skipped. Scanning the same card again produces a new image, so it is never treated as a duplicate. Images from a batch you discarded or removed are picked up again if you drop them back in.

## One reviewer at a time

Only one person can review a batch at a time, so two people never add the same cards twice.

- **Open** claims the batch for you. Following a link to a batch nobody is reviewing claims it automatically.
- **Release** gives the batch up so someone else can take it. Your saved changes stay with the batch. The reviewer or an administrator can release it.
- If someone else is reviewing a batch, the panel offers **View** instead of **Open**. The batch opens read-only with the message that that person is reviewing it; you can look but not change anything.
- An administrator viewing someone else's batch can click **Take over** to become its reviewer.
- If a batch you opened has no reviewer (for example after you released it), click **Review this batch** to start reviewing it again.
- If someone takes the batch over while you have it open, you'll see a warning that you're no longer reviewing it, and your last changes may not have been saved.

## Discard a batch

To throw a whole batch away, click **Discard** in the panel or at the top of the batch page, then confirm in the **Discard batch?** window. Its unsaved cards and their stored images are deleted. The original files stay in the folder's **_processed** subfolder, so you can move them back out to scan them again, unless an administrator has turned on **Delete originals when a batch is closed**. The window tells you which applies.

A batch that hasn't started can't be discarded, because its files are still in the scan folder. Remove the files from the folder instead.

The reviewer or an administrator can discard a batch. A batch nobody is reviewing can be discarded by anyone who is allowed to add scans to the collection.

> [!WARNING]
> Discarding can't be undone from within OmniCard.

## Tips and troubleshooting

- **A batch never appears.** Ask an administrator to check that **Watch folders** is on, the game's folder is **Active**, and its status chip reads **Found**. Files must be JPEG, PNG or TIFF images.
- **A batch stays on Waiting.** It starts only after its files have gone the quiet period without changing. If scanning (or your scanner software's finishing step) is still going, that's expected. Click **Run now** to start it straight away, but only once the scanner software has finished.
- **A batch stays on Waiting to run.** Automatic processing is off. Click **Run**.
- **Batches only process while someone is using the site.** On some servers, background work pauses when the site is idle. An administrator can fix this; the settings page includes a hint about it.
- **Start a new batch.** Save into a new subfolder. Files saved into a subfolder join its batch while it is still waiting; once that batch has started, new files in the same subfolder start a fresh batch, and a number such as *(2)* is added to its name to tell them apart.
- **One file was left in the folder.** If the scanner software still had a file open when the batch started, OmniCard leaves it there and it becomes the next batch.
- **Wrong set or condition for the whole batch?** Use bulk **Edit** to change the cards' properties before adding them. To change the defaults for future batches, ask an administrator.
