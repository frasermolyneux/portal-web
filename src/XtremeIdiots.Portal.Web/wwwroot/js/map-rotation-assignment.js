(function () {
    'use strict';
    const enabled = document.getElementById('AacpEnabled');
    const config = document.getElementById('aacp-config');
    const path = document.getElementById('AacpConfigFilePath');
    if (!enabled || !config || !path) return;
    function update() {
        config.hidden = !enabled.checked;
        path.required = enabled.checked;
    }
    enabled.addEventListener('change', update);
    update();
}());
