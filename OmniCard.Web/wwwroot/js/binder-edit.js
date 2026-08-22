/* Web binder editor — recreates the desktop BinderView: drag/drop placement + swap, layout, page
   add/insert/move/remove, spread navigation, type-ahead, and the card context menu. Talks to
   /api/binder/* (all writes go to inventory.db). Plain IIFE, no framework, matching binder.js. */
(function () {
    "use strict";

    const root = document.getElementById("binderEdit");
    if (!root) return;
    const containerId = parseInt(root.dataset.containerId, 10);

    const parseJson = (id, fallback) => {
        const el = document.getElementById(id);
        try { return el ? JSON.parse(el.textContent) : fallback; } catch { return fallback; }
    };

    let state = parseJson("binder-state", null);
    let unplaced = parseJson("binder-unplaced", []);
    const games = parseJson("binder-games", []);

    let spreadIndex = state ? state.spreadIndex : 0;
    let filter = "";

    // --- API helpers -------------------------------------------------------------------------
    async function apiGet(path) {
        const res = await fetch(path, { headers: { "Accept": "application/json" } });
        if (res.status === 401) { toast("Session expired — reload and re-enter the passphrase."); throw new Error("401"); }
        if (!res.ok) { const e = await safeErr(res); toast(e); throw new Error(e); }
        return res.json();
    }
    async function apiPost(path, body) {
        const res = await fetch(path, {
            method: "POST",
            headers: { "Content-Type": "application/json", "Accept": "application/json" },
            body: JSON.stringify(body || {}),
        });
        if (res.status === 401) { toast("Session expired — reload and re-enter the passphrase."); throw new Error("401"); }
        if (!res.ok) { const e = await safeErr(res); toast(e); throw new Error(e); }
        return res.json();
    }
    async function safeErr(res) {
        try { const j = await res.json(); return j.error || ("Error " + res.status); } catch { return "Error " + res.status; }
    }

    async function refreshAll() {
        const [s, u] = await Promise.all([
            apiGet(`/api/binder/state?containerId=${containerId}&spreadIndex=${spreadIndex}`),
            apiGet(`/api/binder/unplaced?containerId=${containerId}&filter=${encodeURIComponent(filter)}`),
        ]);
        state = s; spreadIndex = s.spreadIndex; unplaced = u.cards;
        render();
    }
    async function refreshState() {
        state = await apiGet(`/api/binder/state?containerId=${containerId}&spreadIndex=${spreadIndex}`);
        spreadIndex = state.spreadIndex;
        renderSpread();
        renderTabs();
    }
    async function refreshUnplaced() {
        const u = await apiGet(`/api/binder/unplaced?containerId=${containerId}&filter=${encodeURIComponent(filter)}`);
        unplaced = u.cards;
        renderUnplaced();
    }

    // --- Rendering ---------------------------------------------------------------------------
    function render() { renderHeader(); renderSpread(); renderTabs(); renderUnplaced(); }

    function renderHeader() {
        document.getElementById("binderTitle").textContent = state.containerName;
        document.getElementById("pageRangeLabel").textContent = state.pageRangeLabel;
        document.getElementById("totalPagesLabel").textContent = "of " + state.totalPages;
        document.getElementById("slotsPerPage").value = state.slotsPerPage;
        document.getElementById("columns").value = state.columns;
    }

    function cardTile(card, opts) {
        const el = document.createElement("div");
        el.className = "edit-tile";
        el.dataset.lot = card.id;
        el.draggable = true;
        el.title = `${card.name}${card.setCode ? " · " + card.setCode : ""}`;
        if (opts && opts.page != null) { el.dataset.page = opts.page; el.dataset.slot = opts.slot; }
        let badges = "";
        if (card.isTraded) badges = `<span class="slot-badge traded">TRADED</span>`;
        else if (card.foil) badges = `<span class="slot-badge foil">FOIL</span>`;
        const price = card.price ? `<span class="price-badge">${card.price}</span>` : "";
        const img = card.imageUrl
            ? `<img src="${card.imageUrl}" loading="lazy" alt="${escapeHtml(card.name)}"/>`
            : `<span class="no-image">${escapeHtml(card.name)}</span>`;
        el.innerHTML = img + badges + price;
        el.addEventListener("dragstart", (e) => {
            const payload = { lotId: card.id, originPage: opts ? opts.page : null, originSlot: opts ? opts.slot : null };
            e.dataTransfer.setData("application/json", JSON.stringify(payload));
            e.dataTransfer.effectAllowed = "move";
        });
        el.addEventListener("dblclick", () => openEditor(card));
        el.addEventListener("contextmenu", (e) => {
            e.preventDefault();
            openContextMenu(e, {
                ids: [card.id], card: card, isPlaced: opts != null,
                hasSlot: opts != null, page: opts ? opts.page : null, slot: opts ? opts.slot : null,
            });
        });
        return el;
    }

    function renderPage(pageNumber, slots, side) {
        const wrap = document.createElement("div");
        wrap.className = "edit-page";
        const header = document.createElement("div");
        header.className = "edit-page-header";
        if (pageNumber != null) {
            header.innerHTML = `<span class="edit-page-num">Page ${pageNumber}</span>`;
            const btns = document.createElement("span");
            btns.className = "edit-page-btns";
            const move = document.createElement("button");
            move.className = "icon-btn"; move.title = "Move this page"; move.textContent = "⤳";
            move.addEventListener("click", () => openMovePage(pageNumber));
            const del = document.createElement("button");
            del.className = "icon-btn"; del.title = "Remove this page"; del.textContent = "🗑";
            del.addEventListener("click", () => removePage(pageNumber));
            btns.appendChild(move); btns.appendChild(del);
            header.appendChild(btns);
        }
        wrap.appendChild(header);

        const grid = document.createElement("div");
        grid.className = "edit-grid";
        grid.style.setProperty("--cols", state.columns);
        if (pageNumber == null) {
            grid.classList.add("empty-page");
        } else {
            slots.forEach((s) => grid.appendChild(renderSlot(pageNumber, s)));
        }
        wrap.appendChild(grid);
        return wrap;
    }

    function renderSlot(pageNumber, slot) {
        const cell = document.createElement("div");
        cell.className = "edit-slot";
        cell.dataset.page = pageNumber;
        cell.dataset.slot = slot.slotIndex;
        if (slot.card) {
            cell.appendChild(cardTile(slot.card, { page: pageNumber, slot: slot.slotIndex }));
        } else {
            cell.classList.add("empty");
            cell.innerHTML = `<span class="slot-empty-label">Empty</span>`;
            cell.addEventListener("contextmenu", (e) => {
                e.preventDefault();
                openContextMenu(e, { ids: [], card: null, isPlaced: false, hasSlot: true, page: pageNumber, slot: slot.slotIndex });
            });
        }
        cell.addEventListener("dragover", (e) => { e.preventDefault(); cell.classList.add("drop-hover"); });
        cell.addEventListener("dragleave", () => cell.classList.remove("drop-hover"));
        cell.addEventListener("drop", (e) => {
            e.preventDefault();
            cell.classList.remove("drop-hover");
            let payload;
            try { payload = JSON.parse(e.dataTransfer.getData("application/json")); } catch { return; }
            if (!payload) return;
            if (payload.originPage === pageNumber && payload.originSlot === slot.slotIndex) return;
            assign(payload.lotId, pageNumber, slot.slotIndex);
        });
        return cell;
    }

    function renderSpread() {
        const spread = document.getElementById("spread");
        spread.innerHTML = "";
        spread.appendChild(renderPage(state.leftPageNumber, state.leftSlots, "left"));
        const spine = document.createElement("div"); spine.className = "edit-spine";
        spread.appendChild(spine);
        spread.appendChild(renderPage(state.rightPageNumber, state.rightSlots, "right"));
        renderHeader();
    }

    function renderTabs() {
        const strip = document.getElementById("spreadTabs");
        strip.innerHTML = "";
        state.spreadTabs.forEach((t) => {
            const b = document.createElement("button");
            b.className = "spread-tab" + (t.isCurrent ? " current" : "");
            b.textContent = t.label;
            b.addEventListener("click", () => { spreadIndex = t.index; refreshState(); });
            strip.appendChild(b);
        });
        document.getElementById("binderFirst").disabled = spreadIndex <= 0;
        document.getElementById("binderPrev").disabled = spreadIndex <= 0;
        document.getElementById("binderNext").disabled = spreadIndex >= state.totalSpreads - 1;
        document.getElementById("binderLast").disabled = spreadIndex >= state.totalSpreads - 1;
    }

    function renderUnplaced() {
        const pool = document.getElementById("unplacedPool");
        pool.innerHTML = "";
        if (unplaced.length === 0) {
            pool.innerHTML = `<p class="pool-empty">No unplaced cards${filter ? " match this filter" : ""}.</p>`;
            return;
        }
        unplaced.forEach((c) => pool.appendChild(cardTile(c, null)));
    }

    // --- Actions -----------------------------------------------------------------------------
    async function assign(lotId, page, slot) {
        await apiPost("/api/binder/assign", { lotId, containerId, page, slot });
        await refreshAll();
    }

    async function unassign(lotId) {
        await apiPost("/api/binder/unassign", { lotId });
        await refreshAll();
    }

    async function applyLayout() {
        const slotsPerPage = parseInt(document.getElementById("slotsPerPage").value, 10);
        const columns = parseInt(document.getElementById("columns").value, 10);
        if (!(slotsPerPage > 0) || !(columns > 0)) { toast("Slots per page and columns must be positive."); return; }
        await apiPost("/api/binder/layout", { containerId, slotsPerPage, columns });
        await refreshState();
    }

    async function addPage(mode) {
        const r = await apiPost("/api/binder/page/add", { containerId, mode });
        spreadIndex = r.spreadIndex;
        await refreshAll();
    }

    async function removePage(pageNumber) {
        if (!confirm("Remove this sheet (both sides)? Its cards return to the Unplaced pool and later pages shift down. This can't be undone.")) return;
        try {
            await apiPost("/api/binder/page/remove", { containerId, page: pageNumber });
            await refreshAll();
        } catch { /* toast already shown */ }
    }

    // --- Insert / Move page modals -----------------------------------------------------------
    async function openInsertPage() {
        const { sheets } = await apiGet(`/api/binder/sheets?containerId=${containerId}`);
        const sel = document.getElementById("insertPosition");
        sel.innerHTML = "";
        sheets.forEach((s) => {
            const label = s.sides === 2 ? `Before pages ${s.firstPage}–${s.firstPage + 1}` : `Before page ${s.firstPage}`;
            sel.appendChild(option(s.sheetIndex, label));
        });
        sel.appendChild(option(sheets.length, "At the end"));
        sel.value = String(sheets.length);
        showModal("modalInsertPage");
    }
    async function confirmInsertPage() {
        const insertIndex = parseInt(document.getElementById("insertPosition").value, 10);
        const doubleSided = document.querySelector('input[name="insertSides"]:checked').value === "double";
        const r = await apiPost("/api/binder/page/insert", { containerId, insertIndex, doubleSided });
        closeModals();
        spreadIndex = r.spreadIndex;
        await refreshAll();
    }

    let movePageFrom = null;
    async function openMovePage(pageNumber) {
        const { sheets } = await apiGet(`/api/binder/sheets?containerId=${containerId}`);
        const moving = sheets.find((s) => s.pages.includes(pageNumber));
        if (!moving) return;
        if (sheets.length <= 1) { toast("There's only one page, so there's nowhere to move it."); return; }
        movePageFrom = pageNumber;
        document.getElementById("movePageTitle").textContent =
            moving.sides === 2 ? `Move pages ${moving.firstPage}–${moving.firstPage + 1}` : `Move page ${moving.firstPage}`;
        const others = sheets.filter((s) => s.sheetIndex !== moving.sheetIndex);
        const sel = document.getElementById("movePosition");
        sel.innerHTML = "";
        others.forEach((s, j) => {
            const label = s.sides === 2 ? `Before pages ${s.firstPage}–${s.firstPage + 1}` : `Before page ${s.firstPage}`;
            sel.appendChild(option(j, label));
        });
        sel.appendChild(option(others.length, "To the end"));
        sel.value = String(others.length);
        showModal("modalMovePage");
    }
    async function confirmMovePage() {
        const toIndex = parseInt(document.getElementById("movePosition").value, 10);
        const r = await apiPost("/api/binder/page/move", { containerId, fromPage: movePageFrom, toIndex });
        closeModals();
        spreadIndex = r.spreadIndex;
        await refreshAll();
    }

    // --- Context menu ------------------------------------------------------------------------
    const menu = document.getElementById("cardContextMenu");
    let ctx = null;

    function openContextMenu(e, sel) {
        ctx = sel;
        const hasCard = sel.ids.length > 0;
        menu.querySelectorAll(".ctx-slot-only").forEach((el) => el.hidden = !sel.hasSlot);
        menu.querySelectorAll(".ctx-placed-only").forEach((el) => el.hidden = !sel.isPlaced);
        // Card actions require a card; when right-clicking an empty slot only Add Missing applies.
        menu.querySelectorAll("button[data-act]").forEach((b) => {
            const act = b.dataset.act;
            if (act === "add-missing") { b.disabled = false; return; }
            b.disabled = !hasCard;
        });
        menu.hidden = false;
        const mw = menu.offsetWidth, mh = menu.offsetHeight;
        menu.style.left = Math.min(e.pageX, window.scrollX + window.innerWidth - mw - 4) + "px";
        menu.style.top = Math.min(e.pageY, window.scrollY + window.innerHeight - mh - 4) + "px";
    }
    function closeContextMenu() { menu.hidden = true; ctx = null; }

    document.addEventListener("click", (e) => { if (!menu.contains(e.target)) closeContextMenu(); });
    document.addEventListener("scroll", closeContextMenu, true);

    menu.addEventListener("click", async (e) => {
        const btn = e.target.closest("button[data-act]");
        if (!btn || btn.disabled || !ctx) return;
        const act = btn.dataset.act;
        const sel = ctx;
        closeContextMenu();
        try { await runAction(act, btn, sel); } catch { /* toast shown */ }
    });

    async function runAction(act, btn, sel) {
        switch (act) {
            case "add-missing": openAddMissing(sel.page, sel.slot); break;
            case "editor": openEditor(sel.card); break;
            case "copy": await navigator.clipboard.writeText(sel.card ? sel.card.name : ""); toast("Copied."); break;
            case "move": openMoveLocation(sel.ids); break;
            case "unassign": await unassign(sel.ids[0]); break;
            case "tags": openTags(sel); break;
            case "list": openListSale(sel); break;
            case "mark-picked": await apiPost("/api/binder/card/mark-picked", { ids: sel.ids }); await refreshAll(); break;
            case "unlist": await apiPost("/api/binder/card/unlist", { ids: sel.ids }); await refreshAll(); break;
            case "cond": await apiPost("/api/binder/card/condition", { ids: sel.ids, value: btn.dataset.val }); await refreshAll(); break;
            case "foil": await apiPost("/api/binder/card/foil", { ids: sel.ids, isFoil: btn.dataset.val === "true" }); await refreshAll(); break;
            case "delete":
                if (confirm(`Delete ${sel.ids.length} card(s)? This can't be undone.`)) { await apiPost("/api/binder/card/delete", { ids: sel.ids }); await refreshAll(); }
                break;
        }
    }

    // --- Modal helpers -----------------------------------------------------------------------
    function showModal(id) {
        document.getElementById("modalBackdrop").hidden = false;
        document.getElementById(id).hidden = false;
    }
    function closeModals() {
        document.getElementById("modalBackdrop").hidden = true;
        document.querySelectorAll(".edit-modal").forEach((m) => (m.hidden = true));
    }
    document.getElementById("modalBackdrop").addEventListener("click", closeModals);
    document.querySelectorAll("[data-close]").forEach((b) => b.addEventListener("click", closeModals));

    // --- Move to Location --------------------------------------------------------------------
    let moveIds = [];
    let locations = [];
    async function openMoveLocation(ids) {
        moveIds = ids;
        if (locations.length === 0) locations = (await apiGet("/api/binder/locations")).locations;
        const sel = document.getElementById("moveLocationSelect");
        sel.innerHTML = "";
        locations.forEach((l) => sel.appendChild(option(l.id, `${l.name} (${l.type})`)));
        updateSectionVisibility();
        sel.onchange = updateSectionVisibility;
        showModal("modalMoveLocation");
    }
    function updateSectionVisibility() {
        const id = parseInt(document.getElementById("moveLocationSelect").value, 10);
        const loc = locations.find((l) => l.id === id);
        document.getElementById("moveSectionWrap").hidden = !(loc && loc.needsSection);
    }
    document.getElementById("moveLocationConfirm").addEventListener("click", async () => {
        const container = parseInt(document.getElementById("moveLocationSelect").value, 10);
        const section = document.getElementById("moveSection").value || null;
        await apiPost("/api/binder/card/move-location", { ids: moveIds, containerId: container, section });
        closeModals(); await refreshAll();
    });

    // --- List for Sale -----------------------------------------------------------------------
    let listIds = [];
    function openListSale(sel) {
        listIds = sel.ids;
        document.getElementById("listPrice").value = sel.card ? (sel.card.marketPriceRaw || 0).toFixed(2) : "0.00";
        document.getElementById("listQty").value = 1;
        showModal("modalListSale");
    }
    document.getElementById("listConfirm").addEventListener("click", async () => {
        const channel = document.getElementById("listChannel").value;
        const price = parseFloat(document.getElementById("listPrice").value);
        const quantity = parseInt(document.getElementById("listQty").value, 10);
        await apiPost("/api/binder/card/list", { ids: listIds, channel, price, quantity });
        closeModals(); await refreshAll();
    });

    // --- Tags --------------------------------------------------------------------------------
    let tagSel = null;
    function openTags(sel) {
        tagSel = sel;
        const list = document.getElementById("tagList");
        list.innerHTML = "";
        const tags = sel.card ? (sel.card.tags || []) : [];
        if (tags.length === 0) list.innerHTML = `<p class="pool-empty">No tags yet.</p>`;
        tags.forEach((t) => {
            const row = document.createElement("div");
            row.className = "tag-edit-row";
            row.innerHTML = `<span class="tag-chip">${escapeHtml(t)}</span>`;
            const rm = document.createElement("button");
            rm.className = "icon-btn"; rm.textContent = "✕"; rm.title = "Remove tag";
            rm.addEventListener("click", async () => {
                await apiPost("/api/binder/card/tags", { ids: sel.ids, tag: t, apply: false });
                await refreshAll(); closeModals();
            });
            row.appendChild(rm);
            list.appendChild(row);
        });
        showModal("modalTags");
    }
    document.getElementById("addTagBtn").addEventListener("click", async () => {
        const name = document.getElementById("newTagName").value.trim();
        if (!name || !tagSel) return;
        await apiPost("/api/binder/card/tags", { ids: tagSel.ids, tag: name, apply: true });
        document.getElementById("newTagName").value = "";
        await refreshAll(); closeModals();
    });

    // --- Card editor -------------------------------------------------------------------------
    let editorCard = null;
    async function openEditor(card) {
        if (!card) return;
        editorCard = card;
        document.getElementById("editorCardName").textContent = card.name;
        document.getElementById("editorCondition").value = card.condition || "NM";
        document.getElementById("editorFoil").checked = !!card.foil;
        document.getElementById("editorPrice").value = card.purchasePrice != null ? card.purchasePrice : "";
        await populateFoilTypes(card);
        showModal("modalEditor");
    }
    async function populateFoilTypes(card) {
        const wrap = document.getElementById("editorFoilTypeWrap");
        const sel = document.getElementById("editorFoilType");
        const foil = document.getElementById("editorFoil").checked;
        wrap.hidden = !foil;
        if (!foil) return;
        const gameId = gameIdOf(card);
        const { foilTypes } = await apiGet(`/api/binder/foil-types?game=${gameId}`);
        sel.innerHTML = "";
        foilTypes.forEach((f) => sel.appendChild(option(f, f)));
        if (card.foilType) sel.value = card.foilType;
    }
    document.getElementById("editorFoil").addEventListener("change", () => populateFoilTypes(editorCard));
    document.getElementById("editorSave").addEventListener("click", async () => {
        const isFoil = document.getElementById("editorFoil").checked;
        const body = {
            id: editorCard.id,
            condition: document.getElementById("editorCondition").value,
            isFoil,
            foilType: isFoil ? document.getElementById("editorFoilType").value : null,
            purchasePrice: parseFloatOrNull(document.getElementById("editorPrice").value),
        };
        await apiPost("/api/binder/card/update", body);
        closeModals(); await refreshAll();
    });

    // --- Add Missing Card --------------------------------------------------------------------
    let addSlot = null;
    let addSelected = null;
    function openAddMissing(page, slot) {
        addSlot = { page, slot };
        addSelected = null;
        const gsel = document.getElementById("addMissingGame");
        if (gsel.options.length === 0) games.forEach((g) => gsel.appendChild(option(g.id, g.name)));
        document.getElementById("addMissingResults").innerHTML = "";
        document.getElementById("addMissingConfirm").disabled = true;
        showModal("modalAddMissing");
    }
    document.getElementById("addMissingSearchBtn").addEventListener("click", async () => {
        const game = parseInt(document.getElementById("addMissingGame").value, 10);
        const query = document.getElementById("addMissingQuery").value;
        const set = document.getElementById("addMissingSet").value;
        const cn = document.getElementById("addMissingCn").value;
        const { results } = await apiGet(
            `/api/binder/catalog/search?game=${game}&query=${encodeURIComponent(query)}&set=${encodeURIComponent(set)}&cn=${encodeURIComponent(cn)}`);
        const box = document.getElementById("addMissingResults");
        box.innerHTML = "";
        addSelected = null;
        document.getElementById("addMissingConfirm").disabled = true;
        results.forEach((m) => {
            const el = document.createElement("div");
            el.className = "add-result";
            el.innerHTML = (m.imageUri ? `<img src="${m.imageUri}" loading="lazy" alt=""/>` : `<span class="no-image">${escapeHtml(m.name)}</span>`) +
                `<span class="add-result-label">${escapeHtml(m.name)} <em>${escapeHtml(m.setCode || "")} ${escapeHtml(m.collectorNumber || "")}</em></span>`;
            el.addEventListener("click", () => {
                box.querySelectorAll(".add-result").forEach((r) => r.classList.remove("selected"));
                el.classList.add("selected");
                addSelected = m;
                document.getElementById("addMissingConfirm").disabled = false;
            });
            box.appendChild(el);
        });
        if (results.length === 0) box.innerHTML = `<p class="pool-empty">No matches.</p>`;
    });
    document.getElementById("addMissingConfirm").addEventListener("click", async () => {
        if (!addSelected || !addSlot) return;
        const game = parseInt(document.getElementById("addMissingGame").value, 10);
        const body = {
            containerId, page: addSlot.page, slot: addSlot.slot,
            game,
            gameSpecificId: addSelected.gameSpecificId,
            name: addSelected.name, setCode: addSelected.setCode || "", setName: addSelected.setName || "",
            collectorNumber: addSelected.collectorNumber || "", rarity: addSelected.rarity || "", imageUri: addSelected.imageUri || null,
            condition: document.getElementById("addMissingCondition").value,
            isFoil: document.getElementById("addMissingFoil").checked,
            foilType: null,
            purchasePrice: parseFloatOrNull(document.getElementById("addMissingPrice").value),
        };
        await apiPost("/api/binder/card/add-missing", body);
        closeModals(); await refreshAll();
    });

    // --- Type-ahead over the unplaced pool ---------------------------------------------------
    let typeBuffer = "";
    let typeTimer = null;
    const pool = document.getElementById("unplacedPool");
    pool.addEventListener("keydown", (e) => {
        if (e.key === "Escape") { resetTypeAhead(); return; }
        if (e.key === "Backspace") { typeBuffer = typeBuffer.slice(0, -1); applyTypeAhead(); e.preventDefault(); return; }
        if (e.key.length !== 1 || e.ctrlKey || e.altKey || e.metaKey) return;
        // Repeated same letter cycles; otherwise extend the prefix.
        if (typeBuffer.length > 0 && typeBuffer.split("").every((c) => c.toLowerCase() === typeBuffer[0].toLowerCase())
            && e.key.toLowerCase() === typeBuffer[0].toLowerCase()) {
            cycleIndex++;
        } else {
            typeBuffer += e.key; cycleIndex = 0;
        }
        applyTypeAhead(); e.preventDefault();
    });
    let cycleIndex = 0;
    function applyTypeAhead() {
        clearTimeout(typeTimer);
        if (typeBuffer.length === 0) { resetTypeAhead(); return; }
        const overlay = document.getElementById("typeAheadOverlay");
        document.getElementById("typeAheadText").textContent = typeBuffer;
        overlay.hidden = false;
        const matches = [];
        unplaced.forEach((c, i) => { if (c.name.toLowerCase().startsWith(typeBuffer.toLowerCase())) matches.push(i); });
        if (matches.length > 0) {
            const idx = matches[cycleIndex % matches.length];
            const tiles = pool.querySelectorAll(".edit-tile");
            tiles.forEach((t) => t.classList.remove("ta-highlight"));
            const tile = tiles[idx];
            if (tile) { tile.classList.add("ta-highlight"); tile.scrollIntoView({ block: "nearest" }); }
        }
        typeTimer = setTimeout(resetTypeAhead, 1200);
    }
    function resetTypeAhead() {
        typeBuffer = ""; cycleIndex = 0;
        document.getElementById("typeAheadOverlay").hidden = true;
        pool.querySelectorAll(".ta-highlight").forEach((t) => t.classList.remove("ta-highlight"));
    }

    // --- Static wiring -----------------------------------------------------------------------
    document.getElementById("binderFirst").addEventListener("click", () => { spreadIndex = 0; refreshState(); });
    document.getElementById("binderPrev").addEventListener("click", () => { if (spreadIndex > 0) { spreadIndex--; refreshState(); } });
    document.getElementById("binderNext").addEventListener("click", () => { if (spreadIndex < state.totalSpreads - 1) { spreadIndex++; refreshState(); } });
    document.getElementById("binderLast").addEventListener("click", () => { spreadIndex = state.totalSpreads - 1; refreshState(); });
    document.getElementById("applyLayout").addEventListener("click", applyLayout);
    document.getElementById("addPageDouble").addEventListener("click", () => addPage("double"));
    document.getElementById("insertPageBtn").addEventListener("click", openInsertPage);
    document.getElementById("insertConfirm").addEventListener("click", confirmInsertPage);
    document.getElementById("movePageConfirm").addEventListener("click", confirmMovePage);

    const addMenuBtn = document.getElementById("addPageMenuBtn");
    const addMenu = document.getElementById("addPageMenu");
    addMenuBtn.addEventListener("click", (e) => { e.stopPropagation(); addMenu.hidden = !addMenu.hidden; });
    addMenu.addEventListener("click", (e) => {
        const b = e.target.closest("button[data-mode]"); if (!b) return;
        addMenu.hidden = true; addPage(b.dataset.mode);
    });
    document.addEventListener("click", () => { addMenu.hidden = true; });

    const filterBox = document.getElementById("unplacedFilter");
    filterBox.addEventListener("keydown", (e) => {
        if (e.key === "Enter") { filter = filterBox.value.trim(); refreshUnplaced(); }
    });

    document.addEventListener("keydown", (e) => { if (e.key === "Escape") { closeModals(); closeContextMenu(); } });

    // --- Utilities ---------------------------------------------------------------------------
    function option(value, label) { const o = document.createElement("option"); o.value = value; o.textContent = label; return o; }
    function parseFloatOrNull(v) { const n = parseFloat(v); return isNaN(n) ? null : n; }
    function gameIdOf(card) {
        return card && card.game != null ? card.game : (games.length ? games[0].id : 0);
    }
    function escapeHtml(s) { return (s || "").replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c])); }
    let toastTimer = null;
    function toast(msg) {
        const t = document.getElementById("toast");
        t.textContent = msg; t.hidden = false;
        clearTimeout(toastTimer);
        toastTimer = setTimeout(() => (t.hidden = true), 3500);
    }

    // Initial paint from server-embedded state.
    if (state) render();
})();
