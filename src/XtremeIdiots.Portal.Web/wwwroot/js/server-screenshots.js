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
    var _table = null;

    function init(options) {
        _serverId = options.serverId;
        _antiForgeryToken = options.antiForgeryToken;
        _tableSelector = options.tableSelector || '#sd-screenshotsTable';
        _previewImageSelector = options.previewImageSelector || '#sd-screenshotPreviewImage';
        _previewModalSelector = options.previewModalSelector || '#sd-screenshotPreviewModal';
        _canDelete = options.canDelete === true;

        $(document).off('screenshots:refresh.serverScreenshots').on('screenshots:refresh.serverScreenshots', function () {
            refresh();
        });
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
                url: '/ServerAdmin/GetScreenshots/' + _serverId + '?skipEntries=0&takeEntries=1000',
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
                        var name = data || row.playerIdentifier || 'Unknown';
                        return escapeHtml(name);
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
                        var viewButton = '<button type="button" class="btn btn-sm btn-outline-primary view-screenshot" data-id="' + row.screenshotId + '"><i class="fa-solid fa-eye"></i> View</button>';
                        var deleteButton = _canDelete
                            ? ' <button type="button" class="btn btn-sm btn-outline-danger delete-screenshot" data-id="' + row.screenshotId + '"><i class="fa-solid fa-trash"></i> Delete</button>'
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

        if (!confirm('Delete this screenshot? This action cannot be undone.')) {
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

    function start() {
        initTable();
        refresh();
    }

    function dispose() {
        if (_tableSelector) {
            $(_tableSelector).off('click', '.view-screenshot');
            $(_tableSelector).off('click', '.delete-screenshot');
        }

        $(document).off('screenshots:refresh.serverScreenshots');

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

    return {
        init: init,
        start: start,
        refresh: refresh,
        dispose: dispose
    };
})();
