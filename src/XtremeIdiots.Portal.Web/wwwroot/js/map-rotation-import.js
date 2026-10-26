(function () {
    const source = document.getElementById('FromGameServer');
    if (!source) return;

    const game = document.getElementById('GameType');
    const server = document.getElementById('GameServerId');
    const path = document.getElementById('ServerCfgPath');
    const fields = document.getElementById('serverImportFields');
    const browse = document.getElementById('serverImportBrowse');

    function updateSource() {
        fields.hidden = !source.checked;
        server.disabled = !source.checked;
        path.disabled = !source.checked;
        server.required = source.checked;
        path.required = source.checked;
        document.querySelectorAll('[data-local-import]').forEach(section => {
            section.hidden = source.checked;
            section.querySelectorAll('input, textarea').forEach(input => {
                input.disabled = source.checked;
            });
        });
        browse.disabled = !source.checked || !server.value;
    }

    function filterServers() {
        Array.from(server.options).forEach(option => {
            if (!option.value) return;
            option.hidden = option.dataset.gameType !== game.value;
            option.disabled = option.hidden;
        });
        const selected = server.options[server.selectedIndex];
        if (selected && selected.disabled) {
            server.value = '';
            path.value = '';
        }
        updateSource();
    }

    source.addEventListener('change', updateSource);
    game.addEventListener('change', filterServers);
    server.addEventListener('change', function () {
        path.value = '';
        updateSource();
    });
    browse.addEventListener('click', function () {
        if (source.checked && server.value) {
            openServerFileBrowser(server.value, 'ServerCfgPath', 'map-rotation-import', '.cfg');
        }
    });
    filterServers();
})();
