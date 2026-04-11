/**
 * Shared RCON utilities used across ViewRcon modules.
 * Provides toast notifications, HTML escaping, refresh badge management, and AJAX helpers.
 */
var RconUtils = (function () {
    'use strict';

    function showToast(type, message, title) {
        toastr.options = {
            closeButton: true,
            progressBar: true,
            positionClass: "toast-top-right",
            timeOut: 5000
        };

        if (type === 'success') {
            toastr.success(message, title || 'Success');
        } else if (type === 'error') {
            toastr.error(message, title || 'Error');
        } else if (type === 'warning') {
            toastr.warning(message, title || 'Warning');
        } else {
            toastr.info(message, title || 'Info');
        }
    }

    function handleAjaxError(xhr, textStatus, errorThrown) {
        console.error('AJAX Error:', { status: xhr.status, statusText: xhr.statusText, error: errorThrown });
        if (xhr.status !== 404) {
            showToast('error', 'Failed to load data. Please refresh the page.');
        }
    }

    function updateRefreshBadge(badgeId, lastRefreshTime) {
        var badge = document.getElementById(badgeId);
        if (badge && lastRefreshTime && typeof portalDate !== 'undefined') {
            badge.textContent = 'Updated ' + portalDate.formatRelativeTime(lastRefreshTime.toISOString());
        }
    }

    function showInfoModal(title, content) {
        var existing = document.getElementById('infoModal');
        if (existing) existing.remove();

        var modalHtml = '<div class="modal fade" id="infoModal" tabindex="-1" aria-labelledby="infoModalLabel" aria-hidden="true">' +
            '<div class="modal-dialog modal-lg modal-dialog-scrollable">' +
            '<div class="modal-content">' +
            '<div class="modal-header">' +
            '<h5 class="modal-title" id="infoModalLabel">' + escapeHtml(title) + '</h5>' +
            '<button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button>' +
            '</div>' +
            '<div class="modal-body">' +
            '<pre class="mb-0" style="font-size: 11px; max-height: 500px; overflow-y: auto;">' + escapeHtml(content) + '</pre>' +
            '</div>' +
            '<div class="modal-footer">' +
            '<button type="button" class="btn btn-secondary" data-bs-dismiss="modal">Close</button>' +
            '</div></div></div></div>';

        document.body.insertAdjacentHTML('beforeend', modalHtml);
        var modal = new bootstrap.Modal(document.getElementById('infoModal'));
        modal.show();

        document.getElementById('infoModal').addEventListener('hidden.bs.modal', function () {
            this.remove();
        });
    }

    return {
        showToast: showToast,
        handleAjaxError: handleAjaxError,
        updateRefreshBadge: updateRefreshBadge,
        showInfoModal: showInfoModal
    };
})();
