function mapRotationTerminalMessage(data, failedMaps) {
    if (data.runtimeStatus === 'Canceled') return 'Operation cancelled.';
    if (data.runtimeStatus !== 'Completed' || data.success === false || failedMaps) return 'Operation failed. Review the details below.';
    if (data.success === true) return 'Completed successfully.';
    return 'Finished. Refresh to check the recorded result.';
}

$(document).ready(function () {
    var $container = $('#operationsContainer');
    if ($container.length === 0) return;

    var pollUrl = $container.data('poll-url');
    if (!pollUrl) return;

    // Collect all pollable instance IDs (from in-progress rows + bootstrap pending)
    var activePolls = {};
    var polling = true;
    var staleThresholdMs = 15 * 60 * 1000;
    var pollStartTime = Date.now();
    var maxPollDurationMs = 10 * 60 * 1000;

    // Find in-progress rows with instance IDs
    $container.find('tr[data-instance-id]').each(function () {
        var instanceId = $(this).data('instance-id');
        if (instanceId && !activePolls[instanceId]) {
            activePolls[instanceId] = {
                $row: $(this),
                $detailRow: $(this).next('.progress-detail-row'),
                failedCount: 0,
                terminal: false
            };
        }
    });

    // Bootstrap pending instance (from TempData, before operation row exists)
    var pendingId = $container.data('pending-instance-id');
    if (pendingId && !activePolls[pendingId]) {
        var $bootstrapRow = $container.find('.pending-bootstrap-row');
        if ($bootstrapRow.length > 0) {
            activePolls[pendingId] = {
                $row: $bootstrapRow,
                $detailRow: $bootstrapRow.next('.progress-detail-row'),
                failedCount: 0,
                terminal: false,
                isBootstrap: true
            };
        }
    }

    if (Object.keys(activePolls).length === 0) {
        updateElapsedTimers();
        setInterval(updateElapsedTimers, 1000);
        return;
    }

    var maxFailedPolls = 15;

    function statusIcon(status) {
        switch (status) {
            case 'Completed':
            case 'Skipped': return '<i class="fa-solid fa-fw fa-check text-success" aria-hidden="true"></i>';
            case 'InProgress': return '<i class="fa-solid fa-fw fa-spinner fa-spin text-info" aria-hidden="true"></i>';
            case 'Failed': return '<i class="fa-solid fa-fw fa-times text-danger" aria-hidden="true"></i>';
            default: return '<i class="fa-solid fa-fw fa-clock text-muted" aria-hidden="true"></i>';
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

    function finishProgress(ctx, data, failedMaps) {
        var succeeded = data.runtimeStatus === 'Completed' && data.success === true && !failedMaps;
        var failed = data.success === false || failedMaps ||
            data.runtimeStatus === 'Failed' || data.runtimeStatus === 'Terminated';
        ctx.$detailRow.find('.progress-label').text(mapRotationTerminalMessage(data, failedMaps));
        ctx.$detailRow.find('.progress-bar')
            .removeClass('progress-bar-animated bg-info bg-secondary bg-success bg-danger bg-warning')
            .addClass(succeeded ? 'bg-success' : failed ? 'bg-danger' : 'bg-secondary')
            .css('width', '100%').attr('aria-valuenow', 100);
        ctx.$row.removeClass('table-info');
        ctx.$row.find('.stale-badge').remove();
        ctx.terminal = true;
        scheduleReloadIfAllDone();
    }

    function formatElapsed(startIso) {
        var start = (window.portalDate && typeof window.portalDate.parseUtc === 'function')
            ? window.portalDate.parseUtc(startIso)
            : new Date(startIso);
        if (!start || isNaN(start.getTime())) return '0m 0s';
        var now = new Date();
        var diffMs = now - start;
        if (diffMs < 0) diffMs = 0;
        var totalSec = Math.floor(diffMs / 1000);
        var min = Math.floor(totalSec / 60);
        var sec = totalSec % 60;
        return min + 'm ' + sec + 's';
    }

    function updateElapsedTimers() {
        $('.elapsed-timer').each(function () {
            var startedAt = $(this).data('started-at');
            if (startedAt) {
                $(this).text(formatElapsed(startedAt));
            }
        });
    }

    function updateStaleIndicator(ctx, data) {
        if (!data || !data.lastUpdatedAt) return;

        var lastUpdated = (window.portalDate && typeof window.portalDate.parseUtc === 'function')
            ? window.portalDate.parseUtc(data.lastUpdatedAt)
            : new Date(data.lastUpdatedAt);
        if (!lastUpdated || isNaN(lastUpdated.getTime())) return;
        var now = new Date();
        var staleSinceUpdate = (now - lastUpdated) > staleThresholdMs;

        var $statusCell = ctx.$row.find('td:eq(1)');
        var $staleBadge = $statusCell.find('.stale-badge');

        if (staleSinceUpdate) {
            if ($staleBadge.length === 0) {
                $statusCell.append(' <span class="badge bg-warning text-dark stale-badge" title="No progress update for over 15 minutes">No recent updates</span>');
            }
        }
        else {
            $staleBadge.remove();
        }
    }

    function updateProgress(instanceId, data) {
        var ctx = activePolls[instanceId];
        if (!ctx) return;

        var $label = ctx.$detailRow.find('.progress-label');
        var $percent = ctx.$detailRow.find('.progress-percent');
        var $bar = ctx.$detailRow.find('.progress-bar');
        var $mapList = ctx.$detailRow.find('.progress-map-list');

        // Handle not_found or error status
        if (!data || data.status === 'not_found') {
            ctx.failedCount++;
            if (ctx.failedCount >= maxFailedPolls) {
                $label.text('Progress is unavailable. Refresh to check the recorded result.');
                $bar.removeClass('progress-bar-animated bg-info').addClass('bg-warning');
                ctx.terminal = true;
                return;
            }
            $label.text(ctx.isBootstrap ? 'Waiting to start...' : 'Waiting for progress...');
            return;
        }

        if (data.status === 'error') {
            ctx.failedCount++;
            if (ctx.failedCount >= maxFailedPolls) {
                $label.text('Connection lost. Refresh to check progress.');
                $bar.removeClass('progress-bar-animated bg-info').addClass('bg-warning');
                ctx.terminal = true;
                return;
            }
            $label.text('Reconnecting...');
            return;
        }

        // status === 'found'
        ctx.failedCount = 0;

        updateStaleIndicator(ctx, data);
        if (data.error) {
            ctx.$row.find('.operation-error')
                .text('This action did not complete successfully. Expand technical details for the recorded error.')
                .removeClass('text-muted').addClass('text-danger');
            var $diagnostics = ctx.$row.find('.operation-diagnostics');
            if ($diagnostics.length === 0) {
                var $details = $('<details>').append($('<summary>').text('Technical details'));
                $diagnostics = $('<pre class="operation-diagnostics small text-wrap text-break mt-2 mb-1">');
                $details.append($diagnostics);
                ctx.$row.find('.operation-error').after($details);
            }
            $diagnostics.text(data.error);
        }

        var isTerminal = data.runtimeStatus === 'Completed' || data.runtimeStatus === 'Failed' ||
                         data.runtimeStatus === 'Terminated' || data.runtimeStatus === 'Canceled';

        if (!data.progress) {
            if (isTerminal) {
                finishProgress(ctx, data, false);
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
            var failedMaps = p.maps?.some(function (map) { return map.status === 'Failed'; });
            finishProgress(ctx, data, failedMaps);
        }

        // Render per-map status
        $mapList.empty();
        if (p.maps && p.maps.length > 0) {
            var $list = $('<div class="list-group list-group-flush"></div>');
            p.maps.forEach(function (map, idx) {
                var $item = $('<div class="list-group-item d-flex align-items-center py-1 px-3"></div>');
                $item.append('<span class="me-2" style="min-width:24px;">' + statusIcon(map.status) + '</span>');
                $item.append($('<span class="badge bg-secondary me-2" style="min-width:28px;">').text(idx + 1));
                $item.append($('<span class="flex-grow-1">').text(map.mapName));
                var label = map.status === 'Skipped' ? 'Ready' : map.status === 'InProgress' ? 'Working' : map.status;
                $item.append($('<span class="badge ' + statusBadge(map.status) + '">').text(label));
                if (map.error) {
                    var messageClass = map.status === 'Failed' ? 'text-danger' : 'text-muted';
                    $item.append($('<small class="' + messageClass + ' ms-2">').text(map.error));
                }
                $list.append($item);
            });
            $mapList.append($list);
        }

    }

    var reloadScheduled = false;
    function scheduleReloadIfAllDone() {
        if (reloadScheduled) return;
        var allDone = true;
        for (var id in activePolls) {
            if (!activePolls[id].terminal) {
                allDone = false;
                break;
            }
        }
        if (allDone) {
            reloadScheduled = true;
            setTimeout(function () { location.reload(); }, 3000);
        }
    }

    function pollAll() {
        if (!polling) return;

        // Check for polling timeout
        if (Date.now() - pollStartTime > maxPollDurationMs) {
            polling = false;
            var $timeoutBanner = $('<div class="alert alert-warning mt-3">' +
                '<i class="fa-solid fa-fw fa-clock"></i> ' +
                '<strong>Live updates paused.</strong> The action may still be running. ' +
                '<a href="javascript:location.reload()" class="alert-link">Refresh to keep watching</a>.' +
                '</div>');
            $container.append($timeoutBanner);
            return;
        }

        var pendingIds = [];
        for (var id in activePolls) {
            if (!activePolls[id].terminal) {
                pendingIds.push(id);
            }
        }

        if (pendingIds.length === 0) {
            polling = false;
            return;
        }

        // Deduplicate and poll each unique instance ID
        var uniqueIds = pendingIds.filter(function (v, i, a) { return a.indexOf(v) === i; });

        var completed = 0;
        uniqueIds.forEach(function (instanceId) {
            fetch(pollUrl + '?instanceId=' + encodeURIComponent(instanceId))
                .then(function (r) { return r.json(); })
                .then(function (data) {
                    updateProgress(instanceId, data);
                })
                .catch(function () {
                    updateProgress(instanceId, { status: 'error' });
                })
                .finally(function () {
                    completed++;
                    if (completed === uniqueIds.length && polling) {
                        setTimeout(pollAll, 3000);
                    }
                });
        });
    }

    // Start elapsed timer updates
    updateElapsedTimers();
    setInterval(updateElapsedTimers, 1000);

    // Start polling
    pollAll();
});
