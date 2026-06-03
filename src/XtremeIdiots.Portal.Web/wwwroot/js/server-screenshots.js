/**
 * Server screenshots module.
 * Handles listing, preview, and deletion of screenshots for a server.
 */
var ServerScreenshots = (function () {
    'use strict';

    var _serverId = null;
    var _antiForgeryToken = null;
    var _tableSelector = null;
    var _previewImageSelector = null;
    var _previewModalSelector = null;
    var _canDelete = false;
    var _canIncludeDeleted = false;
    var _table = null;
    var _pollingIntervalId = null;
    var _pollRequest = null;
    var _pollInFlight = false;
    var _hasPollingBaseline = false;
    var _latestCapturedUtcMs = null;
    var _latestCapturedScreenshotIds = {};

    var POLLING_INTERVAL_MS = 10000;
    var POLLING_PAGE_SIZE = 10;

    function init(options) {
        _serverId = options.serverId;
        _antiForgeryToken = options.antiForgeryToken;
        _tableSelector = options.tableSelector || '#sd-screenshotsTable';
        _previewImageSelector = options.previewImageSelector || '#sd-screenshotPreviewImage';
        _previewModalSelector = options.previewModalSelector || '#sd-screenshotPreviewModal';
        _canDelete = options.canDelete === true;
        _canIncludeDeleted = options.canIncludeDeleted === true;

        $(document).off('screenshots:refresh.serverScreenshots').on('screenshots:refresh.serverScreenshots', function () {
            refresh();
        });

        $('#sd-applyScreenshotFilters').off('click.serverScreenshots').on('click.serverScreenshots', function () {
            refresh();
        });

        $('#sd-resetScreenshotFilters').off('click.serverScreenshots').on('click.serverScreenshots', function () {
            $('#sd-screenshotFilterPlayerName').val('');
            $('#sd-screenshotFilterPlayerIdentifier').val('');
            $('#sd-screenshotFilterSource').val('');
            $('#sd-screenshotFilterCapturedFrom').val('');
            $('#sd-screenshotFilterCapturedTo').val('');
            $('#sd-screenshotFilterIncludeDeleted').prop('checked', false);
            refresh();
        });

        startBackgroundPolling();
    }

    function startBackgroundPolling() {
        if (_pollingIntervalId) {
            clearInterval(_pollingIntervalId);
            _pollingIntervalId = null;
        }

        performPollingCycle(true);

        _pollingIntervalId = setInterval(function () {
            performPollingCycle(false);
        }, POLLING_INTERVAL_MS);
    }

    function performPollingCycle(isBaseline) {
        if (document.visibilityState === 'hidden') {
            return;
        }

        if (_pollInFlight) {
            return;
        }

        _pollInFlight = true;

        _pollRequest = $.ajax({
            type: 'GET',
            url: '/ServerAdmin/GetScreenshots/' + _serverId,
            data: {
                skipEntries: 0,
                takeEntries: POLLING_PAGE_SIZE
            },
            success: function (result) {
                if (!result || !Array.isArray(result.data)) {
                    return;
                }

                var newItems = [];
                var cycleLatestCapturedUtcMs = null;
                var cycleLatestCapturedScreenshotIds = {};

                result.data.forEach(function (item) {
                    if (!item || !item.screenshotId || item.deleted) {
                        return;
                    }

                    var id = item.screenshotId.toString();
                    var capturedUtcMs = item.capturedUtc ? new Date(item.capturedUtc).getTime() : null;
                    if (capturedUtcMs == null || Number.isNaN(capturedUtcMs)) {
                        return;
                    }

                    if (cycleLatestCapturedUtcMs == null || capturedUtcMs > cycleLatestCapturedUtcMs) {
                        cycleLatestCapturedUtcMs = capturedUtcMs;
                        cycleLatestCapturedScreenshotIds = {};
                        cycleLatestCapturedScreenshotIds[id] = true;
                    } else if (capturedUtcMs === cycleLatestCapturedUtcMs) {
                        cycleLatestCapturedScreenshotIds[id] = true;
                    }

                    if (!isBaseline && _hasPollingBaseline && isNewScreenshot(id, capturedUtcMs)) {
                        newItems.push(item);
                    }
                });

                if (isBaseline || !_hasPollingBaseline) {
                    updateLatestCaptured(cycleLatestCapturedUtcMs, cycleLatestCapturedScreenshotIds);
                    _hasPollingBaseline = true;
                    return;
                }

                updateLatestCaptured(cycleLatestCapturedUtcMs, cycleLatestCapturedScreenshotIds);

                if (!newItems.length) {
                    return;
                }

                var firstPlayer = (newItems[0].playerName || newItems[0].playerIdentifier || 'a player').toString();
                var message = newItems.length === 1
                    ? 'A new screenshot is available for ' + escapeHtml(firstPlayer) + '. Open the Screenshots tab to view it.'
                    : newItems.length + ' new screenshots are available. Open the Screenshots tab to view them.';

                RconUtils.showToast('warning', message, 'New Screenshot');

                if (_table) {
                    refresh();
                }
            },
            error: function (xhr) {
                // Polling failures are transient; keep them out of user-facing noise.
                console.warn('Screenshot polling failed', xhr && xhr.status ? xhr.status : 'unknown');
            },
            complete: function () {
                _pollInFlight = false;
                _pollRequest = null;
            }
        });
    }

    function isNewScreenshot(screenshotId, capturedUtcMs) {
        if (_latestCapturedUtcMs == null) {
            return false;
        }

        if (capturedUtcMs > _latestCapturedUtcMs) {
            return true;
        }

        if (capturedUtcMs === _latestCapturedUtcMs && !_latestCapturedScreenshotIds[screenshotId]) {
            return true;
        }

        return false;
    }

    function updateLatestCaptured(capturedUtcMs, idsAtTimestamp) {
        if (capturedUtcMs == null) {
            return;
        }

        if (_latestCapturedUtcMs == null || capturedUtcMs > _latestCapturedUtcMs) {
            _latestCapturedUtcMs = capturedUtcMs;
            _latestCapturedScreenshotIds = idsAtTimestamp || {};
            return;
        }

        if (capturedUtcMs === _latestCapturedUtcMs && idsAtTimestamp) {
            Object.keys(idsAtTimestamp).forEach(function (id) {
                _latestCapturedScreenshotIds[id] = true;
            });
        }
    }

    function initTable() {
        if (_table) {
            return _table;
        }

        _table = $(_tableSelector).DataTable({
            processing: true,
            paging: true,
            pageLength: 25,
            info: true,
            searching: false,
            stateSave: true,
            responsive: true,
            order: [[0, 'desc']],
            ajax: {
                url: '/ServerAdmin/GetScreenshots/' + _serverId,
                data: function () {
                    return buildFilterQuery();
                },
                dataSrc: 'data',
                error: RconUtils.handleAjaxError
            },
            columns: [
                {
                    data: 'capturedUtc',
                    name: 'capturedUtc',
                    render: function (data) {
                        if (!data) {
                            return '-';
                        }
                        var dt = new Date(data);
                        return dt.toLocaleString();
                    }
                },
                {
                    data: 'playerName',
                    name: 'playerName',
                    render: function (data, type, row) {
                        var hasLinkedPlayer = !!(data || row.playerIdentifier);
                        var name = data || row.playerIdentifier || 'Unlinked';
                        if (row.deleted) {
                            return '<span class="text-decoration-line-through text-muted">' + escapeHtml(name) + '</span>';
                        }
                        if (!hasLinkedPlayer) {
                            return '<span class="text-muted">Unlinked</span>';
                        }
                        return escapeHtml(name);
                    }
                },
                {
                    data: null,
                    name: 'linkStatus',
                    render: function (data, type, row) {
                        return renderLinkStatus(row);
                    }
                },
                {
                    data: 'source',
                    name: 'source',
                    render: function (data) {
                        return escapeHtml(data || '-');
                    }
                },
                {
                    data: 'sourceFileName',
                    name: 'sourceFileName',
                    render: function (data) {
                        return escapeHtml(data || '-');
                    }
                },
                {
                    data: 'sizeBytes',
                    name: 'sizeBytes',
                    className: 'text-end',
                    render: function (data) {
                        return formatSize(data || 0);
                    }
                },
                {
                    data: null,
                    name: 'actions',
                    sortable: false,
                    className: 'text-nowrap',
                    render: function (data, type, row) {
                        if (row.deleted) {
                            return '<span class="text-muted">Deleted</span>';
                        }

                        var viewButton = '<button type="button" class="btn btn-sm btn-outline-secondary view-screenshot" data-id="' + row.screenshotId + '"><i class="fa-solid fa-fw fa-eye" aria-hidden="true"></i> View</button>';
                        var deleteButton = _canDelete
                            ? ' <button type="button" class="btn btn-sm btn-outline-danger delete-screenshot" data-id="' + row.screenshotId + '" data-confirm="Are you sure you want to delete this screenshot? This action cannot be undone."><i class="fa-solid fa-fw fa-trash" aria-hidden="true"></i> Delete</button>'
                            : '';

                        return '<div class="btn-group btn-group-sm" role="group">' + viewButton + deleteButton + '</div>';
                    }
                }
            ]
        });

        $(_tableSelector).on('click', '.view-screenshot', function () {
            var screenshotId = $(this).data('id');
            viewScreenshot(screenshotId);
        });

        $(_tableSelector).on('click', '.delete-screenshot', function () {
            var screenshotId = $(this).data('id');
            deleteScreenshot(screenshotId);
        });

        return _table;
    }

    function viewScreenshot(screenshotId) {
        if (!screenshotId) {
            return;
        }

        var imageUrl = '/ServerAdmin/GetScreenshotContent/' + _serverId + '?screenshotId=' + encodeURIComponent(screenshotId);
        var $previewImage = $(_previewImageSelector);
        $previewImage.off('error').on('error', function () {
            RconUtils.showToast('error', 'Unable to load screenshot content');
        });
        $previewImage.attr('src', imageUrl);

        var modal = bootstrap.Modal.getOrCreateInstance(document.querySelector(_previewModalSelector));
        modal.show();
    }

    function deleteScreenshot(screenshotId) {
        if (!screenshotId || !_canDelete) {
            return;
        }

        $.ajax({
            type: 'POST',
            url: '/ServerAdmin/DeleteScreenshot/' + _serverId,
            data: {
                screenshotId: screenshotId,
                __RequestVerificationToken: _antiForgeryToken
            },
            success: function (result) {
                if (result.success) {
                    RconUtils.showToast('success', result.message || 'Screenshot deleted');
                    refresh();
                } else {
                    RconUtils.showToast('error', result.message || 'Failed to delete screenshot');
                }
            },
            error: function (xhr) {
                RconUtils.showToast('error', 'Error: ' + (xhr.responseText || 'Unknown error'));
            }
        });
    }

    function refresh() {
        if (_table) {
            _table.ajax.reload(null, false);
        }
    }

    function buildFilterQuery() {
        var query = {
            skipEntries: 0,
            takeEntries: 1000
        };

        var playerName = ($('#sd-screenshotFilterPlayerName').val() || '').toString().trim();
        var playerIdentifier = ($('#sd-screenshotFilterPlayerIdentifier').val() || '').toString().trim();
        var source = ($('#sd-screenshotFilterSource').val() || '').toString().trim();
        var capturedFrom = ($('#sd-screenshotFilterCapturedFrom').val() || '').toString().trim();
        var capturedTo = ($('#sd-screenshotFilterCapturedTo').val() || '').toString().trim();
        var includeDeleted = _canIncludeDeleted && $('#sd-screenshotFilterIncludeDeleted').is(':checked');

        if (playerName) {
            query.playerName = playerName;
        }

        if (playerIdentifier) {
            query.playerIdentifier = playerIdentifier;
        }

        if (source) {
            query.source = source;
        }

        if (capturedFrom) {
            query.capturedFromUtc = new Date(capturedFrom).toISOString();
        }

        if (capturedTo) {
            query.capturedToUtc = new Date(capturedTo).toISOString();
        }

        if (includeDeleted) {
            query.includeDeleted = true;
        }

        return query;
    }

    function start() {
        initTable();
    }

    function dispose() {
        if (_pollingIntervalId) {
            clearInterval(_pollingIntervalId);
            _pollingIntervalId = null;
        }

        if (_pollRequest && typeof _pollRequest.abort === 'function') {
            _pollRequest.abort();
            _pollRequest = null;
        }

        _pollInFlight = false;

        if (_tableSelector) {
            $(_tableSelector).off('click', '.view-screenshot');
            $(_tableSelector).off('click', '.delete-screenshot');
        }

        $('#sd-applyScreenshotFilters').off('click.serverScreenshots');
        $('#sd-resetScreenshotFilters').off('click.serverScreenshots');

        $(document).off('screenshots:refresh.serverScreenshots');

        _hasPollingBaseline = false;
        _latestCapturedUtcMs = null;
        _latestCapturedScreenshotIds = {};

        if (_table) {
            _table.destroy();
            _table = null;
        }
    }

    function formatSize(bytes) {
        if (bytes < 1024) {
            return bytes + ' B';
        }

        var kb = bytes / 1024;
        if (kb < 1024) {
            return kb.toFixed(1) + ' KB';
        }

        return (kb / 1024).toFixed(1) + ' MB';
    }

    function renderLinkStatus(row) {
        var source = (row.linkSource || '').toString().toLowerCase();
        var confidence = (row.linkConfidence || '').toString().toLowerCase();
        var diagnostics = row.linkDiagnostics ? ' (' + escapeHtml(row.linkDiagnostics) + ')' : '';

        if (source === 'request_match') {
            return '<span class="badge bg-success">Request matched</span>';
        }

        if (source === 'filename_match') {
            return '<span class="badge bg-info text-dark">Filename matched</span>';
        }

        if (source === 'manual') {
            return '<span class="badge bg-primary">Manual link</span>';
        }

        if (source === 'unlinked' || !source) {
            var label = confidence === 'low'
                ? 'Unlinked (low confidence)'
                : 'Unlinked';
            return '<span class="badge bg-secondary">' + label + '</span>' + (diagnostics ? '<span class="text-muted ms-1">' + diagnostics + '</span>' : '');
        }

        return '<span class="badge bg-secondary">' + escapeHtml(source) + '</span>';
    }

    return {
        init: init,
        start: start,
        refresh: refresh,
        dispose: dispose
    };
})();
