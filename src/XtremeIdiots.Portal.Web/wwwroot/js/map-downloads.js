(() => {
    'use strict';
    const root = document.getElementById('mapDownloads');
    if (!root) return;
    const byId = id => document.getElementById(id);
    const member = root.dataset.member || '';
    const selected = new Set();
    const tasks = new Map();
    const chunkSize = 4 * 1024 * 1024;
    let pageHidden = false;
    const query = new URLSearchParams(location.search);
    const rotation = query.get('rotation');
    const aliases = { CallOfDuty2: 'cod2', CallOfDuty4: 'cod4', CallOfDuty4x: 'cod4', cod4x: 'cod4', CallOfDuty5: 'cod5' };
    const game = query.get('game') || 'cod4';
    byId('downloadGame').value = aliases[game] || (['cod2', 'cod4', 'cod5'].includes(game) ? game : 'cod4');
    let page = 1, version = null, path = null, folder = null, manifest = query.get('map'), generation = 0;
    if (byId('downloadGame').value === 'cod2' && manifest) {
        byId('downloadQuery').value = manifest;
        manifest = null;
    }
    const token = root.querySelector('[name="__RequestVerificationToken"]').value;
    const status = message => { byId('downloadStatus').textContent = message; };
    const bytes = value => {
        const unit = [1024, 1048576, 1073741824].filter(threshold => value >= threshold).length;
        return new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 }).format(value / (1024 ** unit)) + ' ' + ['B', 'KiB', 'MiB', 'GiB'][unit];
    };
    const button = (label, action, primary = false) => {
        const element = document.createElement('button');
        element.type = 'button';
        element.className = 'btn btn-sm ' + (primary ? 'btn-primary' : 'btn-outline-secondary');
        element.textContent = label;
        element.addEventListener('click', action);
        return element;
    };
    async function api(action, data, post = false) {
        const url = '/MapDownloads/' + action + (post ? '' : '?' + new URLSearchParams(Object.entries(data).filter(([, value]) => value != null)));
        let response;
        try {
            response = await fetch(url, {
                method: post ? 'POST' : 'GET', credentials: 'same-origin', cache: 'no-store',
                headers: post ? { 'Content-Type': 'application/json', RequestVerificationToken: token } : {},
                body: post ? JSON.stringify(data) : undefined
            });
        } catch {
            throw new Error('Connection interrupted. Try again when you are online.');
        }
        if (!response.ok || response.redirected) {
            let message = 'The request could not complete. Sign in again if your session has ended.';
            try { message = (await response.json()).message || message; } catch { /* Login responses may be HTML. */ }
            const error = new Error(message);
            error.status = response.redirected ? 401 : response.status;
            throw error;
        }
        return response.json();
    }
    function selectable(entry) {
        return entry.isDirectory && /^redirect\/(?:cod[45]\/(?:usermaps|mods)\/[^/]+|cod2\/(?!main$)[^/]+)$/.test(entry.path);
    }
    function updateSelection() {
        byId('downloadSelection').textContent = `${selected.size} of 100 folders selected`;
        byId('downloadZip').disabled = !member || selected.size === 0;
        byId('downloadClear').disabled = selected.size === 0;
        byId('downloadSelectionBar').hidden = !!path || !!manifest || selected.size === 0;
        root.querySelectorAll('[data-select-path]').forEach(input => { input.checked = selected.has(input.dataset.selectPath); });
    }
    function updateView() {
        byId('downloadSearch').hidden = !!path || !!manifest;
        byId('downloadBack').hidden = !path && !manifest;
        byId('downloadContext').textContent = byId('downloadGame').selectedOptions[0].textContent;
        const managerQuery = new URLSearchParams();
        if (manifest) {
            managerQuery.set('game', byId('downloadGame').value);
            managerQuery.set('map', manifest);
        } else if (path) managerQuery.set('path', path);
        byId('downloadManager').href = 'https://maps.xtremeidiots.com/' + (managerQuery.size ? '?' + managerQuery : '');
        updateSelection();
    }
    function choose(entry, input) {
        if (input.checked && selected.size >= 100) {
            input.checked = false;
            status('A ZIP can contain at most 100 folders. Clear a selection first.');
            return;
        }
        if (input.checked) selected.add(entry.path); else selected.delete(entry.path);
        updateSelection();
    }
    async function load(focus = false) {
        const current = ++generation;
        const entries = byId('downloadEntries');
        entries.setAttribute('aria-busy', 'true');
        byId('downloadFolderZip').hidden = true;
        byId('downloadPagination').hidden = true;
        if (byId('downloadLogin')) byId('downloadLogin').hidden = true;
        updateView();
        status('Loading live files…');
        try {
            const data = await loadMetadata();
            if (current !== generation) return;
            version = data.version;
            const files = data.files || data.entries;
            entries.replaceChildren();
            if (manifest) folder = { path: data.path, name: data.name, isDirectory: true };
            byId('downloadHeading').textContent = folder?.name || byId('downloadKind').selectedOptions[0].textContent;
            byId('downloadFolderZip').hidden = !member || !folder || !selectable(folder) || !files.length;
            const zipAvailable = folder ? selectable(folder) && files.length > 0 : files.some(selectable);
            if (byId('downloadLogin')) byId('downloadLogin').hidden = !zipAvailable;
            files.forEach(entry => entries.append(renderEntry(entry, data)));
            byId('downloadPagination').hidden = !!manifest || data.totalCount <= data.pageSize;
            byId('downloadPrevious').disabled = !!manifest || page <= 1;
            byId('downloadNext').disabled = !!manifest || page * data.pageSize >= data.totalCount;
            byId('downloadPage').textContent = manifest ? `${files.length} files` : `Page ${page} · ${data.totalCount} entries`;
            let summary = files.length ? `${data.totalCount} ${data.totalCount === 1 ? 'entry' : 'entries'}` : 'No matching files.';
            if (manifest) summary = `${files.length} ${files.length === 1 ? 'file' : 'files'} · ${bytes(data.bytes)}`;
            status(summary);
            updateSelection();
            if (focus) byId('downloadHeading').focus();
        } catch (error) {
            if (current !== generation) return;
            entries.replaceChildren();
            status(error.message);
            if (error.status === 409) {
                version = null; page = 1;
                entries.append(button('Refresh collection', () => load(true)));
            }
        } finally {
            if (current === generation) entries.setAttribute('aria-busy', 'false');
        }
    }
    function loadMetadata() {
        if (manifest) return api('Manifest', { game: byId('downloadGame').value, mapName: manifest });
        return api(path ? 'Browse' : 'Collection', {
            game: byId('downloadGame').value, kind: byId('downloadKind').value,
            path, search: path ? '' : byId('downloadQuery').value, page, version
        });
    }
    function renderEntry(entry, data) {
        const row = document.createElement('div');
        row.className = 'native-download-entry';
        row.setAttribute('role', 'listitem');
        const label = document.createElement('span');
        label.className = 'native-download-name';
        label.textContent = manifest ? entry.path.slice(data.path.length + 1) : entry.name;
        const size = document.createElement('span');
        size.className = 'native-download-size';
        size.textContent = bytes(entry.bytes);
        const actions = document.createElement('div');
        actions.className = 'd-flex flex-wrap gap-2 align-items-center';
        if (entry.isDirectory) {
            actions.append(button('Browse files', () => {
                path = entry.path;
                folder = entry;
                manifest = /^redirect\/cod[45]\/usermaps\/[^/]+$/.test(path) && byId('downloadKind').value === 'maps'
                    ? entry.name : null;
                page = 1; version = null; void load(true);
            }));
            addFolderTools(actions, entry);
        } else {
            actions.append(button('Save file', () => startFile(entry), true));
        }
        row.append(label, size, actions);
        return row;
    }
    function addFolderTools(container, entry) {
        if (!member || !selectable(entry)) return;
        const label = document.createElement('label');
        label.className = 'form-check d-flex gap-2 align-items-center mb-0';
        const input = document.createElement('input');
        input.type = 'checkbox'; input.className = 'form-check-input';
        input.dataset.selectPath = entry.path;
        input.checked = selected.has(entry.path);
        input.addEventListener('change', () => choose(entry, input));
        label.append(input, document.createTextNode('Select ' + entry.name));
        container.append(label, button('Create folder ZIP', () => createZip([entry.path])));
    }
    root.querySelector('#downloadSearch').addEventListener('submit', event => {
        event.preventDefault(); page = 1; version = null; path = null; folder = null; manifest = null; void load(true);
    });
    ['downloadGame', 'downloadKind'].forEach(id => byId(id).addEventListener('change', () => {
        page = 1; version = null; path = null; folder = null; manifest = null; void load(true);
    }));
    byId('downloadBack').addEventListener('click', () => { path = null; folder = null; manifest = null; page = 1; version = null; void load(true); });
    byId('downloadPrevious').addEventListener('click', () => { page--; void load(true); });
    byId('downloadNext').addEventListener('click', () => { page++; void load(true); });
    byId('downloadClear').addEventListener('click', () => { selected.clear(); updateSelection(); });
    byId('downloadZip').addEventListener('click', () => createZip([...selected]));
    byId('downloadFolderZip').addEventListener('click', () => { if (folder) void createZip([folder.path]); });

    // Only file handles and resource identifiers are persisted, never capability URLs.
    const database = new Promise(resolve => {
        try {
            if (!window.indexedDB) { resolve(null); return; }
            const request = indexedDB.open('portal-map-downloads', 1);
            request.onupgradeneeded = () => request.result.createObjectStore('tasks', { keyPath: 'id' });
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => resolve(null);
            request.onblocked = () => resolve(null);
        } catch { resolve(null); }
    });
    async function store(task, remove = false) {
        const db = await database;
        if (!db) return;
        const { id, owner, resource, name, version: fileVersion, offset, total, handle, expires, job, invalidated } = task;
        const integrity = handle ? task.integrity || [] : undefined;
        try {
            await new Promise(resolve => {
                const transaction = db.transaction('tasks', 'readwrite');
                const objectStore = transaction.objectStore('tasks');
                transaction.oncomplete = () => { task.persisted = !remove; resolve(); };
                transaction.onerror = transaction.onabort = resolve;
                if (remove) { objectStore.delete(id); return; }
                const request = objectStore.get(id);
                request.onsuccess = () => {
                    try {
                        // A delayed ZIP status response must not erase another tab's disk checkpoint.
                        const existing = request.result;
                        if (!existing && task.persisted) { retireTask(task); return; }
                        const value = !handle && existing?.integrity != null
                            ? { ...existing, job, expires }
                            : { id, owner, resource, name, version: fileVersion, offset, total, handle, expires, job, integrity, invalidated };
                        objectStore.put(value);
                    } catch { transaction.abort(); }
                };
            });
        } catch { /* Private browsing/storage policy may prevent persisting file handles. Saving still works. */ }
    }
    function renderTask(task) {
        if (!tasks.has(task.id)) return;
        byId('downloadActivity').hidden = false;
        if (!task.element) {
            task.element = document.createElement('div');
            task.element.className = 'native-download-task';
            byId('downloadTasks').append(task.element);
        }
        const focusedLabel = task.element.contains(document.activeElement) ? document.activeElement.textContent : null;
        const heading = document.createElement('strong');
        heading.textContent = task.name;
        heading.tabIndex = -1;
        const text = document.createElement('p');
        text.className = 'mb-2';
        text.textContent = task.state + (task.total ? ` · ${bytes(task.offset || 0)} / ${bytes(task.total)}` : '');
        const actions = taskActions(task);
        task.element.replaceChildren(heading, text, actions);
        if (focusedLabel) ([...actions.querySelectorAll('button')].find(action => action.textContent === focusedLabel) || heading).focus();
    }
    function taskActions(task) {
        const actions = document.createElement('div');
        actions.className = 'd-flex flex-wrap gap-2';
        if (task.busy) {
            if (task.abort) actions.append(button('Pause download', () => task.abort.abort()));
        } else if (task.resource && !task.terminal) {
            let label = task.job ? 'Save ZIP' : 'Save file';
            if (task.handle) label = 'Resume save';
            actions.append(button(label, () => save(task), true));
            if (task.handle) actions.append(button('Restart save in a new file', () => {
                task.handle = null; task.offset = 0; task.version = null; task.integrity = []; task.restart = true; void save(task);
            }));
            actions.append(button('Use browser download', () => browserDownload(task)));
        }
        actions.append(button(!task.resource && task.busy ? 'Dismiss ZIP task' : 'Dismiss download task', async () => {
            task.abort?.abort(); clearTimeout(task.timer); tasks.delete(task.id);
            task.element.remove();
            byId('downloadActivity').hidden = tasks.size === 0;
            (tasks.size ? byId('downloadTasks') : byId('downloadHeading')).focus();
            await store(task, true);
        }));
        return actions;
    }
    function newTask(data) {
        const task = { id: crypto.randomUUID(), owner: member, offset: 0, ...data };
        tasks.set(task.id, task); renderTask(task); return task;
    }
    function retireTask(task) {
        clearTimeout(task.timer); task.abort?.abort();
        task.state = 'This task completed or was dismissed in another tab, or its destination was reused. Dismiss this local task.';
        task.terminal = true; task.busy = false;
    }
    async function createZip(paths, serverId = null) {
        if (!member || !serverId && (paths.length < 1 || paths.length > 100)) return;
        const task = newTask({ name: serverId ? 'Rotation ZIP' : `ZIP: ${paths.map(value => value.split('/').pop()).join(', ')}`, state: 'Submitting ZIP…', busy: true });
        try {
            const job = await api(serverId ? 'CreateRotationArchive' : 'CreateArchive', serverId ? { gameServerId: serverId } : { paths }, true);
            if (!tasks.has(task.id)) return;
            paths.forEach(value => selected.delete(value)); updateSelection();
            task.job = job.id; task.expires = job.expiresUtc;
            await acceptJob(task, job);
        } catch (error) {
            task.state = error.message + ' Submission was not retried automatically.';
            task.busy = false; task.terminal = true; renderTask(task);
        }
    }
    async function acceptJob(task, job) {
        if (!tasks.has(task.id)) return;
        task.state = 'ZIP ' + job.status;
        task.expires = job.expiresUtc;
        if (new Date(job.expiresUtc) <= new Date() || ['expired', 'failed', 'cancelled'].includes(job.status)) {
            task.state = job.status === 'failed' ? 'ZIP failed. Create a new ZIP to try again.' : 'ZIP expired or cancelled. Create a new ZIP.';
            task.busy = false; task.terminal = true; await store(task, true);
        } else if (job.status === 'complete') {
            task.resource = { archiveId: job.id };
            task.name = job.fileName; task.total = job.bytes;
            task.state = 'ZIP ready to save'; task.busy = false;
            await store(task);
            armExpiry(task);
        } else if (['queued', 'running'].includes(job.status)) {
            task.busy = true; await store(task);
            schedulePoll(task, 2000);
        } else {
            task.state = 'ZIP state unavailable. Create a new ZIP.'; task.busy = false; task.terminal = true;
            await store(task, true);
        }
        renderTask(task);
    }
    function expire(task) {
        if (!tasks.has(task.id) || task.terminal) return;
        clearTimeout(task.timer);
        task.abort?.abort(); task.state = 'ZIP expired. Create a new ZIP.';
        task.busy = false; task.terminal = true; void store(task, true); renderTask(task);
    }
    function armExpiry(task) {
        clearTimeout(task.timer);
        if (!pageHidden && !task.terminal)
            task.timer = setTimeout(() => expire(task), Math.min(2147483647, Math.max(0, new Date(task.expires) - Date.now())));
    }
    function schedulePoll(task, delay) {
        clearTimeout(task.timer);
        if (!pageHidden && !task.terminal) task.timer = setTimeout(() => { void poll(task); }, delay);
    }
    function trackJob(task) {
        if (!task.job || task.terminal) return;
        if (new Date(task.expires) <= new Date()) { expire(task); return; }
        if (task.resource) armExpiry(task); else void poll(task);
    }
    async function poll(task) {
        if (!tasks.has(task.id) || task.terminal || task.polling || pageHidden) return;
        if (new Date(task.expires) <= new Date()) { expire(task); return; }
        task.polling = true;
        clearTimeout(task.timer);
        try {
            await acceptJob(task, await api('ArchiveStatus', { id: task.job }));
        } catch (error) {
            if (!tasks.has(task.id)) return;
            if ([401, 403, 404, 410].includes(error.status)) {
                task.state = error.message; task.busy = false; task.terminal = true;
                await store(task, true);
            } else {
                task.state = 'ZIP status temporarily unavailable; retrying automatically.';
                schedulePoll(task, error.status === 429 ? 30000 : 10000);
            }
            renderTask(task);
        } finally { task.polling = false; }
    }
    function startFile(entry) {
        const task = newTask({ name: entry.name, total: entry.bytes, resource: { path: entry.path, version: entry.version }, state: 'Ready to save' });
        void save(task);
    }
    function validateGrant(grant, task) {
        const url = new URL(grant.url);
        if (url.protocol !== 'https:' || url.username || url.password) throw new Error('A secure download address is required.');
        if (task.version && grant.version !== task.version) throw new Error('Content changed. Dismiss this task, refresh files and start a new download.');
        if (task.resource.version && grant.version !== task.resource.version) throw new Error('Content changed. Refresh files and start a new download.');
        if (!Number.isSafeInteger(grant.bytes) || grant.bytes < 0 || !grant.version || /["\r\n]/.test(grant.version))
            throw new Error('Invalid download metadata.');
        if (task.version && task.total !== grant.bytes) throw new Error('The download size changed. Start a new download.');
        return grant;
    }
    async function browserDownload(task) {
        task.busy = true; task.state = 'Starting browser download…'; renderTask(task);
        try {
            const grant = validateGrant(await api('Grant', task.resource, true), task);
            if (!tasks.has(task.id) || task.terminal) return;
            const link = document.createElement('a');
            link.href = grant.url; link.download = grant.fileName; link.referrerPolicy = 'no-referrer';
            document.body.append(link); link.click(); link.remove();
            task.state = 'Sent to your browser download manager. Check your browser for progress.';
            clearTimeout(task.timer);
            task.terminal = true; await store(task, true);
        } catch (error) { task.state = error.message; }
        task.busy = false; renderTask(task);
    }
    async function save(task) {
        if (task.busy) return;
        if (!window.showSaveFilePicker || !window.isSecureContext || !navigator.locks || !window.crypto?.subtle) { await browserDownload(task); return; }
        task.busy = true; task.state = 'Choose a save location…'; renderTask(task);
        try {
            // Invoke the picker before asynchronous work so browser user activation is retained.
            task.handle ||= await window.showSaveFilePicker({ suggestedName: task.name });
            if (await task.handle.requestPermission({ mode: 'readwrite' }) !== 'granted')
                throw new Error('Save permission was revoked. Choose a new file or use browser download.');
            if (!tasks.has(task.id) || task.terminal) return;
            await navigator.locks.request('portal-native-download-writer', { ifAvailable: true }, lock => {
                if (!lock) throw new Error('Another Portal save is running. Pause it before starting or resuming this download.');
                return transfer(task);
            });
        } catch (error) {
            if (!task.terminal) task.state = error.name === 'AbortError' ? 'Download paused. Resume save to continue.' : error.message;
        } finally {
            task.busy = false; task.abort = null; renderTask(task);
        }
    }
    async function reconcileCheckpoint(task) {
        if (task.restart) {
            await store(task, true); task.restart = false; return;
        }
        const db = await database;
        if (!db) return;
        const record = await new Promise(resolve => {
            const request = db.transaction('tasks').objectStore('tasks').get(task.id);
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => resolve(null);
        });
        if (!record) {
            if (task.persisted) {
                retireTask(task);
                throw new Error(task.state);
            }
            return;
        }
        if (record.invalidated) {
            task.state = 'The destination was reused by another download. Dismiss this task and start a new save.';
            task.terminal = true;
            throw new Error(task.state);
        }
        if (record.owner !== member ||
            record.resource?.path !== task.resource.path || record.resource?.archiveId !== task.resource.archiveId ||
            (record.handle && !await record.handle.isSameEntry(task.handle)))
            throw new Error('The saved resource or location changed in another tab. Restart save in a new file.');
        task.offset = record.offset ?? 0; task.version = record.version; task.total = record.total; task.integrity = record.integrity;
    }
    async function transfer(task) {
        await reconcileCheckpoint(task);
        if (!tasks.has(task.id) || task.terminal) return;
        try {
            let grant = validateGrant(await api('Grant', task.resource, true), task);
            if (!tasks.has(task.id) || task.terminal) return;
            task.version = grant.version; task.total = grant.bytes; task.expires ||= grant.expiresUtc;
            if (!Number.isSafeInteger(task.offset) || task.offset < 0 || task.offset > task.total)
                throw new Error('The saved offset is invalid. Restart save in a new file.');
            task.abort = new AbortController();
            task.state = 'Verifying saved bytes…'; renderTask(task);
            const file = await task.handle.getFile();
            if (!tasks.has(task.id) || task.terminal) return;
            task.abort.signal.throwIfAborted();
            if (file.size < task.offset) throw new Error('The saved file is shorter than expected. Restart save in a new file.');
            await verifyCheckpoint(task, file);
            task.state = 'Saving to disk'; renderTask(task);
            // Checkpoints grow geometrically so prefix copying stays linear in the file size.
            do { grant = await saveCheckpoint(task, grant); } while (task.offset < task.total);
            task.abort.signal.throwIfAborted();
            clearTimeout(task.timer);
            task.state = 'Saved to disk'; task.terminal = true; await store(task, true);
        } catch (error) {
            if (tasks.has(task.id) && task.handle && !task.terminal) await store(task);
            throw error;
        }
    }
    async function checksumBlock(file, start, end) {
        const data = await file.slice(start, end).arrayBuffer();
        const digest = await crypto.subtle.digest('SHA-256', data);
        return [...new Uint8Array(digest)].map(value => value.toString(16).padStart(2, '0')).join('');
    }
    async function verifyCheckpoint(task, file) {
        if (task.offset === 0) { task.integrity = []; return; }
        if (!Array.isArray(task.integrity) || task.integrity.length !== Math.ceil(task.offset / chunkSize))
            throw new Error('Checkpoint integrity is unavailable. Restart save in a new file.');
        for (let index = 0; index < task.integrity.length; index++) {
            task.abort.signal.throwIfAborted();
            const digest = await checksumBlock(file, index * chunkSize, Math.min(task.offset, (index + 1) * chunkSize));
            if (digest !== task.integrity[index])
                throw new Error('The saved file was changed or replaced. Restart save in a new file.');
        }
    }
    async function invalidateDestinations(task) {
        const db = await database;
        if (!db) return;
        const records = await readRecords(db);
        await Promise.all(records.map(async record => {
            if (record.id === task.id || !record.handle || !await record.handle.isSameEntry(task.handle)) return;
            record.invalidated = true; record.persisted = true;
            await store(record);
            const previous = tasks.get(record.id);
            if (previous) {
                clearTimeout(previous.timer);
                previous.state = 'The destination was selected for another download. Dismiss this task and start a new save.';
                previous.terminal = true; renderTask(previous);
            }
        }));
    }
    async function rangeResponse(task, grant, start, end) {
        const response = await fetch(grant.url, {
            credentials: 'omit', referrerPolicy: 'no-referrer', cache: 'no-store', redirect: 'error',
            headers: { Range: `bytes=${start}-${end}`, 'If-Match': `"${task.version}"` },
            signal: task.abort.signal
        });
        if (response.status === 401 || response.status === 403)
            throw new Error('The download link expired or access ended. Resume save to request a new link.');
        if (response.status !== 206 || response.headers.get('Content-Range') !== `bytes ${start}-${end}/${task.total}` ||
            response.headers.get('ETag') !== `"${task.version}"` || !response.body) {
            await response.body?.cancel();
            throw new Error('The server could not safely resume this version. Refresh files or restart in a new file.');
        }
        return response;
    }
    async function writeRange(response, writer, expected, signal) {
        let received = 0;
        const meter = new TransformStream({
            transform(value, controller) {
                received += value.byteLength;
                if (received > expected) throw new Error('Invalid download range length.');
                controller.enqueue(value);
            }
        });
        await response.body.pipeThrough(meter).pipeTo(writer, { preventClose: true, preventAbort: true, signal });
        if (received !== expected) throw new Error('Download interrupted. Resume save to continue.');
    }
    async function saveCheckpoint(task, grant) {
        const limit = Math.min(task.total, Math.max(chunkSize, task.offset * 2));
        let cursor = task.offset, writer;
        try {
            do {
                task.abort.signal.throwIfAborted();
                if (new Date(grant.expiresUtc) - Date.now() < 30000)
                    grant = validateGrant(await api('Grant', task.resource, true), task);
                const end = Math.min(limit - 1, cursor + chunkSize - 1);
                const response = cursor < task.total ? await rangeResponse(task, grant, cursor, end) : null;
                if (!writer) {
                    if (task.offset === 0) await invalidateDestinations(task);
                    writer = await task.handle.createWritable({ keepExistingData: task.offset > 0 });
                    task.abort.signal.throwIfAborted();
                    await writer.truncate(task.offset);
                    await writer.seek(task.offset);
                }
                if (response) await writeRange(response, writer, end - cursor + 1, task.abort.signal);
                cursor = end + 1;
            } while (cursor < limit);
            task.abort.signal.throwIfAborted();
            await writer.close();
            const file = await task.handle.getFile();
            const integrity = [...task.integrity];
            for (let start = task.offset; start < cursor; start += chunkSize) {
                task.abort.signal.throwIfAborted();
                integrity.push(await checksumBlock(file, start, Math.min(cursor, start + chunkSize)));
            }
            task.integrity = integrity;
        } catch (error) {
            await writer?.abort().catch(() => {});
            throw error;
        }
        task.offset = cursor;
        if (tasks.has(task.id) && !task.terminal) await store(task);
        renderTask(task);
        return grant;
    }
    function readRecords(db) {
        return new Promise(resolve => {
            const request = db.transaction('tasks').objectStore('tasks').getAll();
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => resolve([]);
        });
    }
    async function restore() {
        const db = await database;
        if (!db) return;
        const records = await readRecords(db);
        await Promise.all(records.map(async record => {
            if (record.owner !== member || (record.job && new Date(record.expires) <= new Date())) {
                await store(record, true); return;
            }
            let state = record.handle ? 'Download paused. Resume save to continue.' : 'Restoring ZIP…';
            if (record.invalidated) state = 'The destination was reused by another download. Dismiss this task and start a new save.';
            const task = newTask({ ...record, persisted: true, terminal: !!record.invalidated, state });
            trackJob(task);
        }));
    }
    window.addEventListener('pagehide', () => {
        pageHidden = true;
        tasks.forEach(task => { clearTimeout(task.timer); task.abort?.abort(); });
    });
    window.addEventListener('pageshow', event => {
        if (event.persisted) { pageHidden = false; tasks.forEach(trackJob); }
    });
    if (rotation) {
        byId('downloadSearch').hidden = true;
        byId('downloadHeading').textContent = 'Map rotation download';
        byId('downloadContext').textContent = 'Public server maps';
        status('Prepare a ZIP of the available custom maps from this server’s normal rotation or recent 48-hour history. Built-in maps and server mods are excluded.');
        byId('downloadRotationZip').hidden = !member;
        byId('downloadRotationZip').addEventListener('click', async () => {
            byId('downloadRotationZip').disabled = true;
            try { await createZip([], rotation); }
            finally { byId('downloadRotationZip').disabled = false; }
        });
        if (byId('downloadLogin')) byId('downloadLogin').hidden = false;
    } else void load();
    void restore();
})();
