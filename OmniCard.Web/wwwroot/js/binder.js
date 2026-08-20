// Binder view: page-turn flip animation, swipe/keyboard navigation, and tap-to-preview modal.
// Read-only — the "Trade" action just links to the existing /Trade workflow.
(function () {
    'use strict';

    var binder = document.getElementById('binder');
    var book = document.getElementById('binderBook');
    if (!binder || !book) return;

    var totalPages = parseInt(binder.dataset.totalPages, 10) || 1;
    var pages = {};
    book.querySelectorAll('.binder-page').forEach(function (el) {
        pages[parseInt(el.dataset.page, 10)] = el;
    });

    var current = 1;
    var animating = false;

    var elCurrent = document.getElementById('binderCurrent');
    var btnFirst = document.getElementById('binderFirst');
    var btnPrev = document.getElementById('binderPrev');
    var btnNext = document.getElementById('binderNext');
    var btnLast = document.getElementById('binderLast');

    function updateChrome() {
        if (elCurrent) elCurrent.textContent = current;
        var atStart = current <= 1;
        var atEnd = current >= totalPages;
        [btnFirst, btnPrev].forEach(function (b) { if (b) b.disabled = atStart; });
        [btnNext, btnLast].forEach(function (b) { if (b) b.disabled = atEnd; });
    }

    // One page-turn. dir 1 = forward (leaf turns to the left, revealing the next page underneath);
    // dir -1 = backward (target page swings in from the left over the current page).
    function flipTo(target) {
        if (animating || target === current || target < 1 || target > totalPages) return;
        var from = pages[current];
        var to = pages[target];
        if (!from || !to) { current = target; updateChrome(); return; }

        animating = true;
        var dir = target > current ? 1 : -1;
        to.style.display = 'flex';
        from.style.display = 'flex';

        var leaf; // the element that actually rotates
        if (dir === 1) {
            to.style.zIndex = 1;
            from.style.zIndex = 2;
            leaf = from;
            requestAnimationFrame(function () { from.classList.add('flip-left'); });
        } else {
            from.style.zIndex = 1;
            to.style.zIndex = 2;
            leaf = to;
            to.classList.add('no-anim', 'flip-left');
            void to.offsetWidth; // commit the pre-flip state before transitioning
            requestAnimationFrame(function () {
                to.classList.remove('no-anim');
                to.classList.remove('flip-left');
            });
        }

        var done = function () {
            leaf.removeEventListener('transitionend', done);
            clearTimeout(fallback);
            from.classList.remove('flip-left', 'no-anim', 'current');
            to.classList.remove('flip-left', 'no-anim');
            from.style.display = 'none';
            from.style.zIndex = '';
            to.style.zIndex = '';
            to.classList.add('current');
            current = target;
            animating = false;
            updateChrome();
        };
        leaf.addEventListener('transitionend', done);
        var fallback = setTimeout(done, 650); // in case transitionend is dropped
    }

    function next() { flipTo(current + 1); }
    function prev() { flipTo(current - 1); }

    if (btnFirst) btnFirst.addEventListener('click', function () { flipTo(1); });
    if (btnPrev) btnPrev.addEventListener('click', prev);
    if (btnNext) btnNext.addEventListener('click', next);
    if (btnLast) btnLast.addEventListener('click', function () { flipTo(totalPages); });

    // --- Swipe (pointer events, so mouse-drag works on desktop too) ---
    var startX = 0, startY = 0, tracking = false, swiped = false;
    var SWIPE = 50;

    binder.addEventListener('pointerdown', function (e) {
        if (e.button && e.button !== 0) return;
        tracking = true;
        swiped = false;
        startX = e.clientX;
        startY = e.clientY;
    });

    binder.addEventListener('pointermove', function (e) {
        if (!tracking) return;
        var dx = e.clientX - startX;
        var dy = e.clientY - startY;
        if (!swiped && Math.abs(dx) > 10 && Math.abs(dx) > Math.abs(dy)) {
            swiped = true; // horizontal drag — treat as a page swipe, not a tap
        }
        if (swiped) e.preventDefault();
    });

    binder.addEventListener('pointerup', function (e) {
        if (!tracking) return;
        tracking = false;
        if (!swiped) return; // a tap — let the slot click handler run
        var dx = e.clientX - startX;
        if (dx <= -SWIPE) next();
        else if (dx >= SWIPE) prev();
    });

    binder.addEventListener('pointercancel', function () { tracking = false; });

    // --- Preview modal ---
    var modal = document.getElementById('binderModal');
    var data = {};
    try {
        var raw = document.getElementById('binder-data');
        if (raw) data = JSON.parse(raw.textContent);
    } catch (err) { data = {}; }

    var mImage = document.getElementById('modalImage');
    var mName = document.getElementById('modalName');
    var mSet = document.getElementById('modalSet');
    var mDetails = document.getElementById('modalDetails');
    var mTags = document.getElementById('modalTags');
    var mTrade = document.getElementById('modalTrade');
    var mTraded = document.getElementById('modalTraded');
    var mTcg = document.getElementById('modalTcg');
    var mDetailsLink = document.getElementById('modalDetailsLink');

    function row(label, value) {
        if (value === null || value === undefined || value === '') return '';
        var tr = document.createElement('tr');
        var th = document.createElement('th');
        th.textContent = label;
        var td = document.createElement('td');
        td.textContent = value;
        tr.appendChild(th);
        tr.appendChild(td);
        return tr;
    }

    function openModal(id) {
        var c = data[id];
        if (!c || !modal) return;

        if (c.imageUrl) {
            mImage.src = c.imageUrl;
            mImage.alt = c.name;
            mImage.hidden = false;
        } else {
            mImage.hidden = true;
            mImage.removeAttribute('src');
        }

        mName.textContent = c.name;
        mSet.textContent = c.setName + ' (' + c.setCode + ')';

        mDetails.innerHTML = '';
        [
            row('Collector #', c.number),
            row('Rarity', c.rarity),
            row('Color', c.color),
            row('Type', c.cardType),
            row('Foil', c.foil ? 'Yes' : 'No'),
            row('Condition', c.condition),
            row('Price', c.price)
        ].forEach(function (r) { if (r) mDetails.appendChild(r); });

        mTags.innerHTML = '';
        (c.tags || []).forEach(function (t) {
            var span = document.createElement('span');
            span.className = 'tag-chip';
            span.textContent = t;
            mTags.appendChild(span);
        });

        if (mTcg) {
            if (c.tcgPlayerUrl) {
                mTcg.href = c.tcgPlayerUrl;
                mTcg.hidden = false;
            } else {
                mTcg.hidden = true;
            }
        }

        mDetailsLink.href = '/card/' + id;
        if (c.isTraded) {
            mTrade.hidden = true;
            mTraded.hidden = false;
        } else {
            mTrade.hidden = false;
            mTraded.hidden = true;
            mTrade.href = '/Trade?lotId=' + id;
        }

        modal.hidden = false;
        document.body.classList.add('modal-open');
    }

    function closeModal() {
        if (!modal) return;
        modal.hidden = true;
        document.body.classList.remove('modal-open');
    }

    book.addEventListener('click', function (e) {
        var slot = e.target.closest('.binder-slot.filled');
        if (!slot) return;
        openModal(slot.dataset.lot);
    });

    if (modal) {
        modal.addEventListener('click', function (e) {
            if (e.target.hasAttribute('data-close')) closeModal();
        });
    }

    // --- Keyboard ---
    document.addEventListener('keydown', function (e) {
        if (modal && !modal.hidden) {
            if (e.key === 'Escape') closeModal();
            return;
        }
        if (e.key === 'ArrowRight') next();
        else if (e.key === 'ArrowLeft') prev();
    });

    updateChrome();
})();
