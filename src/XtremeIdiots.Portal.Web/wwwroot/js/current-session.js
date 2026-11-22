var CurrentSession = (function () {
    'use strict';

    function format(seconds) {
        if (typeof seconds !== 'number' || !Number.isSafeInteger(seconds) || seconds < 0) return 'Unknown';
        var minutes = Math.floor(seconds / 60);
        return Math.floor(minutes / 60) + 'h ' + (minutes % 60) + 'm';
    }

    return { format: format };
})();
