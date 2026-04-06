(function (global, $) {
    function initMapRotationEditor(options) {
        const cfg = Object.assign({
            searchInputId: 'mapSearch',
            listContainerId: 'selectedMapsList',
            hiddenContainerId: 'mapIdsContainer',
            searchUrl: '/MapSearch/Maps',
            gameType: '',
            initialMaps: [] // Array of { id, text, imageUrl }
        }, options || {});

        const $searchInput = $('#' + cfg.searchInputId);
        const $listContainer = $('#' + cfg.listContainerId);
        const $hiddenContainer = $('#' + cfg.hiddenContainerId);

        if ($searchInput.length === 0) return;

        let selectedMaps = []; // Array of { id, text, imageUrl }
        let timer = null;

        // Suggestions dropdown
        const $suggestions = $('<div class="map-suggestions list-group position-absolute bg-white shadow" style="z-index:1050; max-height:300px; overflow-y:auto; display:none; width:100%;"></div>');
        $searchInput.parent().css('position', 'relative');
        $searchInput.after($suggestions);

        // Initialize with existing maps (edit mode)
        if (cfg.initialMaps && cfg.initialMaps.length > 0) {
            cfg.initialMaps.forEach(m => selectedMaps.push(m));
            renderList();
        }

        function renderList() {
            $listContainer.empty();
            $hiddenContainer.empty();

            selectedMaps.forEach((map, index) => {
                // Hidden input for form submission
                $hiddenContainer.append(
                    $('<input>').attr({ type: 'hidden', name: 'MapIds[' + index + ']', value: map.id })
                );

                // Visual card
                const $card = $('<div class="d-flex align-items-center border rounded p-2 mb-2 bg-light" draggable="true"></div>');
                $card.attr('data-index', index);
                $card.attr('data-map-id', map.id);

                $card.append('<span class="badge bg-secondary me-2" style="min-width:28px;">' + (index + 1) + '</span>');
                $card.append($('<img>').attr('src', map.imageUrl || '/images/noimage.jpg').attr('alt', map.text).css({ width: '48px', height: '32px', objectFit: 'cover', borderRadius: '4px' }).addClass('me-2'));
                $card.append($('<span>').addClass('flex-grow-1').text(map.text));

                const $removeBtn = $('<button type="button" class="btn btn-outline-danger btn-sm ms-2"><i class="fa-solid fa-times"></i></button>');
                $removeBtn.on('click', function () {
                    selectedMaps.splice(index, 1);
                    renderList();
                });
                $card.append($removeBtn);

                // Drag and drop
                $card.on('dragstart', function (e) {
                    e.originalEvent.dataTransfer.setData('text/plain', index.toString());
                    $(this).addClass('opacity-50');
                });
                $card.on('dragend', function () {
                    $(this).removeClass('opacity-50');
                });
                $card.on('dragover', function (e) {
                    e.preventDefault();
                    $(this).addClass('border-primary');
                });
                $card.on('dragleave', function () {
                    $(this).removeClass('border-primary');
                });
                $card.on('drop', function (e) {
                    e.preventDefault();
                    $(this).removeClass('border-primary');
                    const fromIndex = parseInt(e.originalEvent.dataTransfer.getData('text/plain'));
                    const toIndex = parseInt($(this).attr('data-index'));
                    if (fromIndex !== toIndex) {
                        const [moved] = selectedMaps.splice(fromIndex, 1);
                        selectedMaps.splice(toIndex, 0, moved);
                        renderList();
                    }
                });

                $listContainer.append($card);
            });
        }

        function clearSuggestions() {
            $suggestions.hide().empty();
        }

        function addMap(map) {
            if (selectedMaps.some(m => m.id === map.id)) return; // prevent duplicates
            selectedMaps.push(map);
            renderList();
            $searchInput.val('');
            clearSuggestions();
        }

        function getGameType() {
            // Read current game type from dropdown (for Create) or from config (for Edit)
            var el = document.getElementById(cfg.gameTypeInputId || 'GameType');
            return el ? el.value : (cfg.gameType || '');
        }

        function search(term) {
            if (!term || term.length < 2) { clearSuggestions(); return; }

            var currentGameType = getGameType();
            const url = cfg.searchUrl + '?term=' + encodeURIComponent(term) + (currentGameType ? '&gameType=' + encodeURIComponent(currentGameType) : '');

            fetch(url)
                .then(r => r.json())
                .then(results => {
                    $suggestions.empty();
                    if (!Array.isArray(results) || results.length === 0) { clearSuggestions(); return; }

                    results.forEach((r, i) => {
                        // Skip already selected
                        if (selectedMaps.some(m => m.id === r.id)) return;

                        const $item = $('<button type="button" class="list-group-item list-group-item-action py-2 px-3 d-flex align-items-center" role="option"></button>');
                        $item.attr('data-value', r.id);
                        $item.append($('<img>').attr('src', r.imageUrl || '/images/noimage.jpg').css({ width: '40px', height: '28px', objectFit: 'cover', borderRadius: '3px' }).addClass('me-2'));
                        $item.append($('<span>').text(r.text));
                        $item.on('click', function () {
                            addMap({ id: r.id, text: r.text, imageUrl: r.imageUrl });
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

        $searchInput.on('input', function () {
            clearTimeout(timer);
            const term = $(this).val().trim();
            timer = setTimeout(() => search(term), 300);
        });

        $searchInput.on('keydown', function (e) {
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

        $(document).on('click.mapSearch', function (e) {
            if (!$(e.target).closest('#' + cfg.searchInputId).length && !$(e.target).closest('.map-suggestions').length) {
                clearSuggestions();
            }
        });

        // Clear maps when game type changes (Create mode)
        var $gameTypeSelect = $('#' + (cfg.gameTypeInputId || 'GameType'));
        if ($gameTypeSelect.length > 0) {
            $gameTypeSelect.on('change', function () {
                if (selectedMaps.length > 0 && !confirm('Changing game type will clear all selected maps. Continue?')) {
                    // Revert — but we don't have the old value easily, so just warn
                    return;
                }
                selectedMaps.length = 0;
                renderList();
                $searchInput.val('');
                clearSuggestions();
            });
        }
    }

    global.initMapRotationEditor = initMapRotationEditor;
})(window, jQuery);
