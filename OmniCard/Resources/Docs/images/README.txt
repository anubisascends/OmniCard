OmniCard documentation screenshots
==================================

Drop PNG screenshots into this folder using the exact filenames below. The help viewer
auto-detects them: if a file exists it replaces the dashed placeholder box in the docs;
if it's missing, the placeholder simply stays. No HTML editing required — just add the
file and (re)build so it copies to the output "Docs\images" folder.

Expected filenames (each maps to a placeholder in index.html):

  overview.png             The main window on first launch
  main-window-regions.png  Main window with the menu / toolbar / sidebar / status bar labelled
  dashboard.png            The Dashboard tab (financial tiles + charts + stats)
  collection.png           The Collection tab in Cards mode
  scanner.png              The Scanner tab mid-scan (queue + match)
  binder.png               The binder page-slot editor
  inventory.png            The sealed-product Inventory view
  orders-kanban.png        The Sales > Orders kanban board
  ebay-connect.png         The eBay sign-in / connect window
  upgrade-deck.png         The Upgrade Deck dialog (cut list + add list, deck source at top)

To add a NEW placeholder elsewhere in the docs, add this to index.html where you want it:

  <figure class="shot" data-img="my-shot.png"><span>Screenshot placeholder — images/my-shot.png</span></figure>

...then drop images/my-shot.png in here.

Recommended: PNG, ~1000-1400px wide. Large images scale down to fit automatically.
