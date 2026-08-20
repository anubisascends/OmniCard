// Client-side quick-search + sort for the card-tile collection views (search results, location).
// Operates on the already-rendered .card-tile anchors — no round trips. Each tile carries the
// sortable/searchable values as data-* attributes; preferences persist per page in localStorage.
(function () {
    'use strict';

    var toolbar = document.querySelector('.collection-toolbar');
    var grid = document.querySelector('.card-tiles');
    if (!toolbar || !grid) return;

    var filterInput = toolbar.querySelector('.cv-filter');
    var sortSelect = toolbar.querySelector('.cv-sort');
    var dirBtn = toolbar.querySelector('.cv-dir');
    var countEl = toolbar.querySelector('.cv-count');

    var tiles = Array.from(grid.querySelectorAll('.card-tile'));

    // Lower rank = better condition, so ascending puts the best copies first.
    var CONDITION_RANK = { M: 0, NM: 1, LP: 2, MP: 3, HP: 4, DMG: 5, PO: 5, PLD: 2 };

    var STORE_KEY = 'omnicard_cv:' + location.pathname;
    var state = { sort: 'name', dir: 'asc', q: '' };
    try {
        var saved = JSON.parse(localStorage.getItem(STORE_KEY));
        if (saved) state = Object.assign(state, saved);
    } catch (e) { /* ignore corrupt prefs */ }

    if (sortSelect) sortSelect.value = state.sort;
    if (filterInput) filterInput.value = state.q;
    updateDirBtn();

    function save() {
        try { localStorage.setItem(STORE_KEY, JSON.stringify(state)); } catch (e) { /* quota/full */ }
    }

    function num(v) {
        if (v === '' || v == null) return null;
        var n = parseFloat(v);
        return isNaN(n) ? null : n;
    }

    function condRank(c) {
        if (!c) return 98;
        var key = c.trim().toUpperCase();
        return key in CONDITION_RANK ? CONDITION_RANK[key] : 97; // unknown conditions sort last
    }

    function collate(a, b) {
        return (a || '').localeCompare(b || '', undefined, { sensitivity: 'base', numeric: true });
    }

    function compare(a, b) {
        var cmp = 0;
        switch (state.sort) {
            case 'price': {
                var ap = num(a.dataset.price), bp = num(b.dataset.price);
                // Cards with no known price always sort to the bottom, regardless of direction.
                if (ap == null && bp == null) cmp = 0;
                else if (ap == null) return 1;
                else if (bp == null) return -1;
                else cmp = ap - bp;
                break;
            }
            case 'condition': cmp = condRank(a.dataset.cond) - condRank(b.dataset.cond); break;
            case 'foil': cmp = (a.dataset.foil === '1' ? 1 : 0) - (b.dataset.foil === '1' ? 1 : 0); break;
            case 'set': cmp = collate(a.dataset.set, b.dataset.set) || collate(a.dataset.cn, b.dataset.cn); break;
            case 'cn': cmp = collate(a.dataset.cn, b.dataset.cn); break;
            default: cmp = collate(a.dataset.name, b.dataset.name);
        }
        if (cmp === 0) cmp = collate(a.dataset.name, b.dataset.name); // stable tiebreaker
        return state.dir === 'desc' ? -cmp : cmp;
    }

    function applyFilter() {
        var q = (state.q || '').trim().toLowerCase();
        var shown = 0;
        tiles.forEach(function (t) {
            var hay = (t.dataset.name + ' ' + t.dataset.set + ' ' + t.dataset.setname + ' ' + t.dataset.cn).toLowerCase();
            var match = !q || hay.indexOf(q) !== -1;
            t.hidden = !match;
            if (match) shown++;
        });
        if (countEl) countEl.textContent = q ? (shown + ' of ' + tiles.length) : '';
    }

    function applySort() {
        tiles.slice().sort(compare).forEach(function (t) { grid.appendChild(t); });
    }

    function updateDirBtn() {
        if (!dirBtn) return;
        var asc = state.dir === 'asc';
        dirBtn.textContent = asc ? '↑ Asc' : '↓ Desc';
        dirBtn.setAttribute('aria-label', asc ? 'Sorting ascending — click for descending' : 'Sorting descending — click for ascending');
        dirBtn.dataset.dir = state.dir;
    }

    if (filterInput) filterInput.addEventListener('input', function () {
        state.q = filterInput.value; applyFilter(); save();
    });
    if (sortSelect) sortSelect.addEventListener('change', function () {
        state.sort = sortSelect.value; applySort(); save();
    });
    if (dirBtn) dirBtn.addEventListener('click', function () {
        state.dir = state.dir === 'asc' ? 'desc' : 'asc'; updateDirBtn(); applySort(); save();
    });

    applyFilter();
    applySort();
})();
