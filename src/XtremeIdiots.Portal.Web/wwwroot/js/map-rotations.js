(() => {
    'use strict';
    const root = document.getElementById('publicRotations');
    if (!root) return;
    const game = document.getElementById('rotationGame');
    const server = document.getElementById('rotationServer');
    function filter() {
        for (const option of server.options) {
            option.hidden = !!game.value && !!option.value && option.dataset.game !== game.value;
            option.disabled = option.hidden;
        }
        if (server.selectedOptions[0]?.disabled) server.value = '';
        let visible = 0;
        root.querySelectorAll('.public-map-rotation').forEach(section => {
            section.hidden = (!!game.value && section.dataset.game !== game.value)
                || (!!server.value && section.dataset.server !== server.value);
            if (!section.hidden) visible++;
        });
        document.getElementById('rotationEmpty').hidden = visible > 0;
    }
    game.addEventListener('change', filter);
    server.addEventListener('change', filter);
})();
