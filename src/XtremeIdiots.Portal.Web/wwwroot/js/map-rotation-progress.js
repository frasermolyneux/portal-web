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

    function formatElapsed(startIso) {
        var start = new Date(startIso);
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

        var lastUpdated = new Date(data.lastUpdatedAt);
        var now = new Date();
        var staleSinceUpdate = (now - lastUpdated) > staleThresholdMs;

        var $statusCell = ctx.$row.find('td:eq(1)');
        var $staleBadge = $statusCell.find('.stale-badge');

        if (staleSinceUpdate) {
            if ($staleBadge.length === 0) {
                $statusCell.append(' <span class="badge bg-warning text-dark stale-badge" title="Orchestration has not updated for over 15 minutes — may be stuck">⚠️ Stale</span>');
            }
        }
    }

    function renderOrchestrationStatus(ctx, data) {
        var $orchStatus = ctx.$detailRow.find('.progress-orchestration-status');
        if (!data) {
            $orchStatus.html('');
            return;
        }

        var html = '<small class="text-muted">';
        html += '<strong>Orchestration:</strong> ' + (data.runtimeStatus || '—');
        if (data.createdAt) html += ' | Created: ' + portalDate.formatDateTime(data.createdAt);
        if (data.lastUpdatedAt) html += ' | Last updated: ' + portalDate.formatDateTime(data.lastUpdatedAt);
        html += '</small>';
        $orchStatus.html(html);
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
                $label.text('Orchestration not found. The operation may have completed or not started.');
                $bar.removeClass('progress-bar-animated bg-info').addClass('bg-warning');
                ctx.terminal = true;
                scheduleReloadIfAllDone();
                return;
            }
            $label.text(ctx.isBootstrap ? 'Waiting for orchestration to start...' : 'Waiting for progress data...');
            return;
        }

        if (data.status === 'error') {
            ctx.failedCount++;
            if (ctx.failedCount >= maxFailedPolls) {
                $label.text('Unable to reach sync service. Please refresh the page.');
                $bar.removeClass('progress-bar-animated bg-info').addClass('bg-danger');
                ctx.terminal = true;
                scheduleReloadIfAllDone();
                return;
            }
            $label.text('Retrying connection to sync service...');
            return;
        }

        // status === 'found'
        ctx.failedCount = 0;

        renderOrchestrationStatus(ctx, data);
        updateStaleIndicator(ctx, data);

        var isTerminal = data.runtimeStatus === 'Completed' || data.runtimeStatus === 'Failed' ||
                         data.runtimeStatus === 'Terminated' || data.runtimeStatus === 'Canceled';

        if (!data.progress) {
            if (isTerminal) {
                $label.text('Operation ' + data.runtimeStatus.toLowerCase());
                $bar.removeClass('progress-bar-animated bg-info').addClass(data.runtimeStatus === 'Completed' ? 'bg-success' : 'bg-danger');
                $bar.css('width', '100%');
                ctx.terminal = true;
                scheduleReloadIfAllDone();
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
            ctx.terminal = true;
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
                $item.append($('<span class="badge ' + statusBadge(map.status) + '">').text(map.status));
                if (map.error) {
                    $item.append($('<small class="text-danger ms-2">').text(map.error));
                }
                $list.append($item);
            });
            $mapList.append($list);
        }

        if (isTerminal) {
            scheduleReloadIfAllDone();
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
                '<strong>Polling timed out.</strong> The operation may still be running. ' +
                '<a href="javascript:location.reload()" class="alert-link">Refresh to check status</a> or cancel the operation manually.' +
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
