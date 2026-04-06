$(document).ready(function () {
    var $container = $('#progressContainer');
    if ($container.length === 0) return;

    var instanceId = $container.data('instance-id');
    var pollUrl = $container.data('poll-url');
    if (!instanceId || !pollUrl) return;

    var $bar = $('#progressBar');
    var $label = $('#progressLabel');
    var $percent = $('#progressPercent');
    var $mapList = $('#mapStatusList');
    var polling = true;

    function statusIcon(status) {
        switch (status) {
            case 'Completed': return '<i class="fa-solid fa-check text-success"></i>';
            case 'InProgress': return '<i class="fa-solid fa-spinner fa-spin text-info"></i>';
            case 'Failed': return '<i class="fa-solid fa-times text-danger"></i>';
            case 'Skipped': return '<i class="fa-solid fa-forward text-muted"></i>';
            default: return '<i class="fa-solid fa-clock text-muted"></i>';
        }
    }

    function statusBadge(status) {
        switch (status) {
            case 'Completed': return 'bg-success';
            case 'InProgress': return 'bg-info';
            case 'Failed': return 'bg-danger';
            case 'Skipped': return 'bg-secondary';
            default: return 'bg-secondary';
        }
    }

    var failedPollCount = 0;
    var maxFailedPolls = 10;

    function updateProgress(data) {
        // Handle terminal states even without progress data
        if (!data || data.status === 'unknown' || data.status === 'not_found') {
            failedPollCount++;
            if (failedPollCount >= maxFailedPolls) {
                $label.text('Unable to retrieve progress. The operation may have completed.');
                $bar.removeClass('progress-bar-animated bg-info').addClass('bg-warning');
                polling = false;
                setTimeout(function () { location.reload(); }, 3000);
                return;
            }
            $label.text('Waiting for progress data...');
            return;
        }

        failedPollCount = 0; // Reset on successful response

        // Check for terminal runtime status BEFORE checking progress
        var isTerminal = data.runtimeStatus === 'Completed' || data.runtimeStatus === 'Failed' ||
                         data.runtimeStatus === 'Terminated' || data.runtimeStatus === 'Canceled';

        if (!data.progress) {
            if (isTerminal) {
                $label.text('Operation ' + data.runtimeStatus.toLowerCase());
                $bar.removeClass('progress-bar-animated bg-info').addClass(data.runtimeStatus === 'Completed' ? 'bg-success' : 'bg-danger');
                $bar.css('width', '100%');
                polling = false;
                setTimeout(function () { location.reload(); }, 2000);
                return;
            }
            $label.text('Operation starting...');
            return;
        }

        var p = data.progress;
        var pct = p.totalMaps > 0 ? Math.round((p.completedMaps / p.totalMaps) * 100) : (isTerminal ? 100 : 0);

        $label.text(p.operation + ': ' + p.completedMaps + ' of ' + p.totalMaps + ' items');
        $percent.text(pct + '%');
        $bar.css('width', pct + '%').attr('aria-valuenow', pct);

        if (isTerminal) {
            $bar.removeClass('progress-bar-animated bg-info').addClass(data.runtimeStatus === 'Completed' ? 'bg-success' : 'bg-danger');
            polling = false;
            setTimeout(function () { location.reload(); }, 2000);
        }

        // Render per-map/step status
        $mapList.empty();
        if (p.maps && p.maps.length > 0) {
            var $list = $('<div class="list-group list-group-flush"></div>');
            p.maps.forEach(function (map, idx) {
                var $item = $('<div class="list-group-item d-flex align-items-center py-2 px-3"></div>');
                $item.append('<span class="me-2" style="min-width:24px;">' + statusIcon(map.status) + '</span>');
                $item.append($('<span class="badge bg-secondary me-2" style="min-width:28px;">').text(idx + 1));
                $item.append($('<span class="flex-grow-1">').text(map.mapName));
                $item.append($('<span class="badge ' + statusBadge(map.status) + '">').text(map.status));
                if (map.error) {
                    $item.append($('<small class="text-danger ms-2">').text(map.error));
                }
                $list.append($item);
            });
            $mapList.append($list);
        }
    }

    function poll() {
        if (!polling) return;

        fetch(pollUrl + '?instanceId=' + encodeURIComponent(instanceId))
            .then(function (r) { return r.json(); })
            .then(function (data) {
                updateProgress(data);
                if (polling) {
                    setTimeout(poll, 3000);
                } else {
                    // Refresh the page after completion to update badges
                    setTimeout(function () { location.reload(); }, 2000);
                }
            })
            .catch(function () {
                failedPollCount++;
                if (failedPollCount >= maxFailedPolls) {
                    $label.text('Lost connection to sync service. Refreshing...');
                    $bar.removeClass('progress-bar-animated bg-info').addClass('bg-warning');
                    polling = false;
                    setTimeout(function () { location.reload(); }, 3000);
                    return;
                }
                if (polling) setTimeout(poll, 5000);
            });
    }

    // Start polling
    poll();
});
