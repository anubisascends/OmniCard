// Client-side table sorting. Click any <th class="sortable"> to sort.
(function () {
    document.addEventListener('click', function (e) {
        var th = e.target.closest('th.sortable');
        if (!th) return;

        var table = th.closest('table');
        var thead = table.querySelector('thead');
        var tbody = table.querySelector('tbody');
        if (!tbody) return;

        var colIndex = Array.from(th.parentNode.children).indexOf(th);
        var currentDir = th.getAttribute('data-sort-dir');
        var newDir = currentDir === 'asc' ? 'desc' : 'asc';

        // Clear sort indicators on sibling headers
        thead.querySelectorAll('th.sortable').forEach(function (h) {
            h.removeAttribute('data-sort-dir');
        });
        th.setAttribute('data-sort-dir', newDir);

        var rows = Array.from(tbody.querySelectorAll('tr'));
        var isNumeric = th.hasAttribute('data-sort-numeric');

        rows.sort(function (a, b) {
            var aText = (a.children[colIndex] || {}).textContent || '';
            var bText = (b.children[colIndex] || {}).textContent || '';

            var cmp;
            if (isNumeric) {
                var aNum = parseFloat(aText.replace(/[^0-9.\-]/g, '')) || 0;
                var bNum = parseFloat(bText.replace(/[^0-9.\-]/g, '')) || 0;
                cmp = aNum - bNum;
            } else {
                cmp = aText.localeCompare(bText, undefined, { sensitivity: 'base', numeric: true });
            }

            return newDir === 'desc' ? -cmp : cmp;
        });

        rows.forEach(function (row) { tbody.appendChild(row); });
    });
})();
