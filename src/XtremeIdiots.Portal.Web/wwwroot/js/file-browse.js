// File Transport Browser Component
// Usage: openFileTransportBrowser(gameServerId, targetInputId, fileFilterOrOptions?)
//   gameServerId: GUID of the game server
//   targetInputId: ID of the input element to populate with selected path
//   fileFilterOrOptions: optional file extension filter (e.g. '.log') or options object
//     selectionMode: 'file' (default) or 'directory'
//     initialPath: directory to open first

(function () {
    var _gameServerId = null;
    var _targetInputId = null;
    var _fileFilter = null;
    var _selectionMode = 'file';
    var _currentPath = '/';
    var _selectedPath = null;
    var _modal = null;

    function openBrowser(gameServerId, targetInputId, fileFilterOrOptions) {
        var options = normalizeOptions(fileFilterOrOptions);

        _gameServerId = gameServerId;
        _targetInputId = targetInputId;
        _fileFilter = options.fileFilter;
        _selectionMode = options.selectionMode;
        _currentPath = options.initialPath;
        _selectedPath = null;

        if (!_modal) {
            _modal = new bootstrap.Modal(document.getElementById('fileBrowseModal'));
        }

        updateModalLabels();
        updateSelectButton();
        _modal.show();
        navigateTo(_currentPath);
    }

    window.openFileTransportBrowser = openBrowser;

    function normalizeOptions(fileFilterOrOptions) {
        var defaultOptions = {
            fileFilter: null,
            selectionMode: 'file',
            initialPath: '/'
        };

        if (!fileFilterOrOptions) {
            return defaultOptions;
        }

        if (typeof fileFilterOrOptions === 'string') {
            defaultOptions.fileFilter = fileFilterOrOptions;
            return defaultOptions;
        }

        if (typeof fileFilterOrOptions === 'object') {
            defaultOptions.fileFilter = fileFilterOrOptions.fileFilter || null;
            defaultOptions.selectionMode = fileFilterOrOptions.selectionMode === 'directory' ? 'directory' : 'file';
            defaultOptions.initialPath = normalizeClientPath(fileFilterOrOptions.initialPath);
        }

        return defaultOptions;
    }

    function normalizeClientPath(path) {
        if (!path || typeof path !== 'string') {
            return '/';
        }

        var normalized = path.replace(/\\/g, '/').trim();
        if (!normalized) {
            return '/';
        }

        if (!normalized.startsWith('/')) {
            normalized = '/' + normalized;
        }

        if (normalized.length > 1 && normalized.endsWith('/')) {
            normalized = normalized.slice(0, -1);
        }

        return normalized;
    }

    function updateModalLabels() {
        var title = document.getElementById('fileBrowseModalLabel');
        var selectBtn = document.getElementById('fileBrowseSelectBtn');

        if (title) {
            title.innerHTML = _selectionMode === 'directory'
                ? '<i class="fa-solid fa-fw fa-folder-tree me-2"></i>Browse Server Folders'
                : '<i class="fa-solid fa-fw fa-folder-open me-2"></i>Browse Server Files';
        }

        if (selectBtn) {
            selectBtn.innerHTML = _selectionMode === 'directory'
                ? '<i class="fa-solid fa-fw fa-check"></i>Select Folder'
                : '<i class="fa-solid fa-fw fa-check"></i>Select';
        }
    }

    function navigateTo(path) {
        var loading = document.getElementById('fileBrowseLoading');
        var error = document.getElementById('fileBrowseError');
        var container = document.getElementById('fileBrowseListingContainer');

        loading.style.display = '';
        error.style.display = 'none';
        container.style.display = 'none';
        _selectedPath = _selectionMode === 'directory' ? normalizeClientPath(path) : null;
        updateSelectButton();

        fetch('/api/file-browse/' + _gameServerId + '/browse?path=' + encodeURIComponent(path))
            .then(function (response) {
                if (response.status === 403) throw new Error('You do not have permission to browse this server\'s files.');
                if (!response.ok) throw new Error('Failed to browse directory (HTTP ' + response.status + ')');
                var contentType = response.headers.get('content-type') || '';
                if (!contentType.includes('application/json')) throw new Error('Unexpected response from server.');
                return response.json();
            })
            .then(function (data) {
                loading.style.display = 'none';
                container.style.display = '';
                _currentPath = data.currentPath;
                if (_selectionMode === 'directory') {
                    _selectedPath = data.currentPath;
                    updateSelectButton();
                }
                renderBreadcrumb(data.currentPath);
                renderListing(data);
            })
            .catch(function (err) {
                loading.style.display = 'none';
                error.style.display = '';
                error.textContent = err.message || 'Failed to connect to the file transport server.';
            });
    }

    function renderBreadcrumb(currentPath) {
        var breadcrumb = document.getElementById('fileBrowseBreadcrumb');
        breadcrumb.innerHTML = '';

        var segments = currentPath.split('/').filter(function (s) { return s.length > 0; });

        // Root
        var rootLi = document.createElement('li');
        rootLi.className = 'breadcrumb-item' + (segments.length === 0 ? ' active' : '');
        if (segments.length === 0) {
            rootLi.textContent = '/';
        } else {
            var rootLink = document.createElement('a');
            rootLink.href = '#';
            rootLink.textContent = '/';
            rootLink.onclick = function (e) { e.preventDefault(); navigateTo('/'); };
            rootLi.appendChild(rootLink);
        }
        breadcrumb.appendChild(rootLi);

        // Path segments
        var buildPath = '';
        segments.forEach(function (seg, idx) {
            buildPath += '/' + seg;
            var li = document.createElement('li');
            var isLast = idx === segments.length - 1;
            li.className = 'breadcrumb-item' + (isLast ? ' active' : '');

            if (isLast) {
                li.textContent = seg;
            } else {
                var link = document.createElement('a');
                link.href = '#';
                link.textContent = seg;
                var navPath = buildPath;
                link.onclick = function (e) { e.preventDefault(); navigateTo(navPath); };
                li.appendChild(link);
            }
            breadcrumb.appendChild(li);
        });
    }

    function renderListing(data) {
        var tbody = document.getElementById('fileBrowseListingBody');
        var emptyMsg = document.getElementById('fileBrowseEmptyMessage');
        tbody.innerHTML = '';

        // Parent directory link
        if (data.parentPath !== null && data.parentPath !== undefined) {
            var parentRow = document.createElement('tr');
            parentRow.style.cursor = 'pointer';
            parentRow.onclick = function () { navigateTo(data.parentPath); };
            parentRow.innerHTML =
                '<td><i class="fa-solid fa-arrow-up text-muted"></i></td>' +
                '<td>..</td><td></td><td></td>';
            tbody.appendChild(parentRow);
        }

        if (data.items.length === 0) {
            emptyMsg.style.display = '';
            return;
        }
        emptyMsg.style.display = 'none';

        data.items.forEach(function (item) {
            var row = document.createElement('tr');
            var isDir = item.type === 'Directory' || item.type === 1;

            if (isDir) {
                row.style.cursor = 'pointer';
                row.onclick = function () { navigateTo(item.fullPath); };
                row.innerHTML =
                    '<td><i class="fa-solid fa-folder text-warning"></i></td>' +
                    '<td>' + escapeHtml(item.name) + '</td>' +
                    '<td></td>' +
                    '<td>' + formatDate(item.modified) + '</td>';
            } else {
                var matchesFilter = !_fileFilter || item.name.toLowerCase().endsWith(_fileFilter.toLowerCase());
                row.style.cursor = matchesFilter ? 'pointer' : 'default';
                if (!matchesFilter) row.classList.add('opacity-50');

                row.onclick = matchesFilter ? function () {
                    // Deselect previous
                    var prev = tbody.querySelector('.table-primary');
                    if (prev) prev.classList.remove('table-primary');
                    row.classList.add('table-primary');
                    _selectedPath = item.fullPath;
                    updateSelectButton();
                } : null;

                row.innerHTML =
                    '<td><i class="fa-regular fa-file text-muted"></i></td>' +
                    '<td>' + escapeHtml(item.name) + '</td>' +
                    '<td class="text-end">' + formatSize(item.size) + '</td>' +
                    '<td>' + formatDate(item.modified) + '</td>';
            }

            tbody.appendChild(row);
        });
    }

    function updateSelectButton() {
        var btn = document.getElementById('fileBrowseSelectBtn');
        var pathDisplay = document.getElementById('fileBrowseSelectedPath');
        btn.disabled = !_selectedPath;
        if (_selectionMode === 'directory' && _selectedPath) {
            pathDisplay.textContent = 'Selected folder: ' + _selectedPath;
            return;
        }

        pathDisplay.textContent = _selectedPath ? _selectedPath : '';
    }

    // Select button click
    document.addEventListener('DOMContentLoaded', function () {
        var selectBtn = document.getElementById('fileBrowseSelectBtn');
        if (selectBtn) {
            selectBtn.addEventListener('click', function () {
                if (_selectedPath && _targetInputId) {
                    var input = document.getElementById(_targetInputId);
                    if (input) input.value = _selectedPath;
                }
                if (_modal) _modal.hide();
            });
        }
    });

    function escapeHtml(text) {
        var div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    function formatSize(bytes) {
        if (bytes === null || bytes === undefined) return '';
        if (bytes < 1024) return bytes + ' B';
        if (bytes < 1048576) return (bytes / 1024).toFixed(1) + ' KB';
        return (bytes / 1048576).toFixed(1) + ' MB';
    }

    function formatDate(dateStr) {
        if (!dateStr) return '';
        var d = new Date(dateStr);
        if (isNaN(d.getTime())) return '';
        return d.toLocaleDateString() + ' ' + d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    }
})();