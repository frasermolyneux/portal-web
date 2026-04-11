// Map Manager - Initialize DataTables with responsive for all tables
$(document).ready(function () {
    // Current Map Rotation Table
    const mapRotationTable = $('#mapRotationTable');
    if (mapRotationTable.length && mapRotationTable.find('tbody tr').length > 0) {
        mapRotationTable.DataTable({
            responsive: {
                details: {
                    type: 'inline',
                    target: 'tr'
                }
            },
            autoWidth: false,
            paging: true,
            pageLength: 25,
            order: [[0, 'asc']],
            columnDefs: [
                { targets: 0, responsivePriority: 2, orderable: true },  // # (sort order)
                { targets: 1, responsivePriority: 1, orderable: true },  // Name - always visible
                { targets: 2, responsivePriority: 5, orderable: false }, // Map Files
                { targets: 3, responsivePriority: 3, orderable: false }, // Remote Status
                { targets: 4, responsivePriority: 6, orderable: false }, // Popularity
                { targets: 5, responsivePriority: 7, orderable: false }  // Image
            ]
        });
    }

    // Remote Server Maps Table
    const remoteMapsTable = $('#remoteMapsTable');
    if (remoteMapsTable.length && remoteMapsTable.find('tbody tr').length > 0) {
        remoteMapsTable.DataTable({
            responsive: {
                details: {
                    type: 'inline',
                    target: 'tr'
                }
            },
            autoWidth: false,
            paging: true,
            pageLength: 25,
            order: [[0, 'asc']],
            columnDefs: [
                { targets: 0, responsivePriority: 1, orderable: true },  // Name
                { targets: 1, responsivePriority: 4, orderable: false }, // Path
                { targets: 2, responsivePriority: 3, orderable: false }, // Rotation Status
                { targets: 3, responsivePriority: 5, orderable: false }, // Health
                { targets: 4, responsivePriority: 6, orderable: false }, // Modified
                { targets: 5, responsivePriority: 2, orderable: false }  // Actions
            ]
        });
    }

    // Push Map to Remote - Map Search
    initPushMapSearch();
});

function initPushMapSearch() {
    const $input = $('#MapName');
    if ($input.length === 0) return;

    $(document).off('click.pushMapSearch');

    let timer = null;
    const $wrapper = $input.parent();
    $wrapper.css('position', 'relative');

    const $suggestions = $('<div class="map-suggestions list-group position-absolute bg-white shadow" style="z-index:1050; max-height:300px; overflow-y:auto; display:none; width:100%;"></div>');
    $input.after($suggestions);

    function clearSuggestions() {
        $suggestions.hide().empty();
    }

    function search(term) {
        if (!term || term.length < 2) { clearSuggestions(); return; }

        const gameTypeAttr = document.getElementById('pushMapGameType');
        const gt = gameTypeAttr ? gameTypeAttr.value : '';
        const url = '/MapSearch/Maps?term=' + encodeURIComponent(term) + (gt ? '&gameType=' + encodeURIComponent(gt) : '');

        fetch(url)
            .then(r => r.json())
            .then(results => {
                $suggestions.empty();
                if (!Array.isArray(results) || results.length === 0) { clearSuggestions(); return; }

                results.forEach(r => {
                    const $item = $('<button type="button" class="list-group-item list-group-item-action py-2 px-3 d-flex align-items-center" role="option"></button>');
                    $item.append($('<img>').attr('src', r.imageUrl || '/images/noimage.jpg').css({ width: '40px', height: '28px', objectFit: 'cover', borderRadius: '3px' }).addClass('me-2'));
                    $item.append($('<span>').text(r.text));
                    $item.on('click', function () {
                        $input.val(r.text);
                        clearSuggestions();
                    });
                    $suggestions.append($item);
                });

                if ($suggestions.children().length > 0) {
                    $suggestions.show();
                } else {
                    clearSuggestions();
                }
            })
            .catch(() => clearSuggestions());
    }

    $input.on('input', function () {
        clearTimeout(timer);
        const term = $(this).val().trim();
        timer = setTimeout(() => search(term), 300);
    });

    $input.on('keydown', function (e) {
        if (!$suggestions.is(':visible')) return;
        const $items = $suggestions.find('.list-group-item');
        if ($items.length === 0) return;
        let idx = $items.index($items.filter('.active'));
        if (e.key === 'ArrowDown') { e.preventDefault(); idx = (idx + 1) % $items.length; }
        else if (e.key === 'ArrowUp') { e.preventDefault(); idx = (idx <= 0 ? $items.length - 1 : idx - 1); }
        else if (e.key === 'Enter') { e.preventDefault(); if (idx >= 0) $items.eq(idx).click(); return; }
        else if (e.key === 'Escape') { clearSuggestions(); return; }
        else return;
        $items.removeClass('active');
        $items.eq(idx).addClass('active')[0].scrollIntoView({ block: 'nearest' });
    });

    $(document).on('click.pushMapSearch', function (e) {
        if (!$(e.target).closest('#MapName').length && !$(e.target).closest('.map-suggestions').length) {
            clearSuggestions();
        }
    });
}
