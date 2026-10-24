(() => {
    'use strict';
    const root = document.getElementById('mapDownloads');
    if (!root) return;
    const byId = id => document.getElementById(id);
    const member = root.dataset.member || '';
    const selected = new Set();
    const tasks = new Map();
    const query = new URLSearchParams(location.search);
    const aliases = { CallOfDuty2: 'cod2', CallOfDuty4: 'cod4', CallOfDuty4x: 'cod4', cod4x: 'cod4', CallOfDuty5: 'cod5' };
    const game = query.get('game') || 'cod4';
    byId('downloadGame').value = aliases[game] || (['cod2', 'cod4', 'cod5'].includes(game) ? game : 'cod4');
    let page = 1, version = null, path = null, manifest = query.get('map'), generation = 0;
    if (byId('downloadGame').value === 'cod2' && manifest) {
        byId('downloadQuery').value = manifest;
        manifest = null;
    }
    const token = root.querySelector('[name="__RequestVerificationToken"]').value;
    const status = message => { byId('downloadStatus').textContent = message; };
    const bytes = value => {
        const unit = value >= 1073741824 ? 3 : value >= 1048576 ? 2 : value >= 1024 ? 1 : 0;
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
        byId('downloadSelection').textContent = `${selected.size} of 15 folders selected`;
        byId('downloadZip').disabled = !member || selected.size === 0;
        byId('downloadClear').disabled = selected.size === 0;
        root.querySelectorAll('[data-select-path]').forEach(input => { input.checked = selected.has(input.dataset.selectPath); });
    }
    function choose(entry, input) {
        if (input.checked && selected.size >= 15) {
            input.checked = false;
            status('A ZIP can contain at most 15 folders. Clear a selection first.');
            return;
        }
        if (input.checked) selected.add(entry.path); else selected.delete(entry.path);
        updateSelection();
    }
    async function load(focus = false) {
        const current = ++generation;
        const entries = byId('downloadEntries');
        entries.setAttribute('aria-busy', 'true');
        status('Loading live files…');
        try {
            const data = manifest
                ? await api('Manifest', { game: byId('downloadGame').value, mapName: manifest })
                : await api(path ? 'Browse' : 'Collection', {
                    game: byId('downloadGame').value, kind: byId('downloadKind').value,
                    path, search: byId('downloadQuery').value, page, version
                });
            if (current !== generation) return;
            version = data.version;
            const files = data.files || data.entries;
            entries.replaceChildren();
            byId('downloadHeading').textContent = manifest ? data.name : path || 'Collection';
            byId('downloadBack').hidden = !path && !manifest;
            if (manifest) {
                const folder = { path: data.path, name: data.name, isDirectory: true };
                const tools = document.createElement('div');
                tools.className = 'd-flex gap-2 mb-2';
                addFolderTools(tools, folder);
                entries.append(tools);
            }
            for (const entry of files) {
                const row = document.createElement('div');
                row.className = 'native-download-entry';
                const label = document.createElement('span');
                label.className = 'native-download-name';
                label.textContent = manifest ? entry.path.slice(data.path.length + 1) : entry.name;
                row.append(label);
                const size = document.createElement('span');
                size.textContent = bytes(entry.bytes);
                row.append(size);
                const actions = document.createElement('div');
                actions.className = 'd-flex flex-wrap gap-2 align-items-center';
                if (entry.isDirectory) {
                    actions.append(button('Browse files', () => {
                        path = entry.path;
                        manifest = /^redirect\/cod[45]\/usermaps\/[^/]+$/.test(path) && byId('downloadKind').value === 'maps'
                            ? entry.name : null;
                        page = 1; version = null; load(true);
                    }));
                    addFolderTools(actions, entry);
                } else {
                    actions.append(button('Save file', () => startFile(entry), true));
                }
                row.append(actions);
                entries.append(row);
            }
            byId('downloadPrevious').disabled = !!manifest || page <= 1;
            byId('downloadNext').disabled = !!manifest || page * data.pageSize >= data.totalCount;
            byId('downloadPage').textContent = manifest ? `${files.length} files` : `Page ${page} · ${data.totalCount} entries`;
            status(manifest ? `${files.length} live files · ${bytes(data.bytes)} total` : files.length ? 'Live files loaded.' : 'No matching files.');
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
        event.preventDefault(); page = 1; version = null; path = null; manifest = null; load(true);
    });
    ['downloadGame', 'downloadKind'].forEach(id => byId(id).addEventListener('change', () => {
        page = 1; version = null; path = null; manifest = null; load(true);
    }));
    byId('downloadBack').addEventListener('click', () => { path = null; manifest = null; page = 1; version = null; load(true); });
    byId('downloadPrevious').addEventListener('click', () => { page--; load(true); });
    byId('downloadNext').addEventListener('click', () => { page++; load(true); });
    byId('downloadClear').addEventListener('click', () => { selected.clear(); updateSelection(); });
    byId('downloadZip').addEventListener('click', () => createZip([...selected]));

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
        const { id, owner, resource, name, version: fileVersion, offset, total, handle, expires, job } = task;
        try {
            await new Promise(resolve => {
                const transaction = db.transaction('tasks', 'readwrite');
                const objectStore = transaction.objectStore('tasks');
                if (remove) objectStore.delete(id);
                else objectStore.put({ id, owner, resource, name, version: fileVersion, offset, total, handle, expires, job });
                transaction.oncomplete = transaction.onerror = transaction.onabort = resolve;
            });
        } catch { /* Private browsing/storage policy may prevent persisting file handles. Saving still works. */ }
    }
    function renderTask(task) {
        if (!tasks.has(task.id)) return;
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
        const actions = document.createElement('div');
        actions.className = 'd-flex flex-wrap gap-2';
        if (task.busy) {
            if (task.abort) actions.append(button('Pause download', () => task.abort.abort()));
        } else if (task.resource && !task.terminal) {
            actions.append(button(task.handle ? 'Resume save' : task.job ? 'Save ZIP' : 'Save file', () => save(task), true));
            if (task.handle) actions.append(button('Restart save in a new file', () => {
                task.handle = null; task.offset = 0; task.version = null; save(task);
            }));
            actions.append(button('Use browser download', () => browserDownload(task)));
        }
        actions.append(button(!task.resource && task.busy ? 'Dismiss ZIP task' : 'Dismiss download task', async () => {
            task.abort?.abort(); clearTimeout(task.timer); tasks.delete(task.id);
            task.element.remove(); byId('downloadTasks').focus(); await store(task, true);
        }));
        task.element.replaceChildren(heading, text, actions);
        if (focusedLabel) ([...actions.querySelectorAll('button')].find(action => action.textContent === focusedLabel) || heading).focus();
    }
    function newTask(data) {
        const task = { id: crypto.randomUUID(), owner: member, offset: 0, ...data };
        tasks.set(task.id, task); renderTask(task); return task;
    }
    async function createZip(paths) {
        if (!member || paths.length < 1 || paths.length > 15) return;
        const task = newTask({ name: `ZIP: ${paths.map(value => value.split('/').pop()).join(', ')}`, state: 'Submitting ZIP…', busy: true });
        try {
            const job = await api('CreateArchive', { paths }, true);
            if (!tasks.has(task.id)) return;
            selected.clear(); updateSelection();
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
            task.timer = setTimeout(() => expire(task), Math.min(2147483647, Math.max(0, new Date(task.expires) - Date.now())));
        } else if (['queued', 'running'].includes(job.status)) {
            task.busy = true; await store(task);
            task.timer = setTimeout(() => poll(task), 2000);
        } else {
            task.state = 'ZIP state unavailable. Create a new ZIP.'; task.busy = false; task.terminal = true;
            await store(task, true);
        }
        renderTask(task);
    }
    function expire(task) {
        if (!tasks.has(task.id)) return;
        task.abort?.abort(); task.state = 'ZIP expired. Create a new ZIP.';
        task.busy = false; task.terminal = true; store(task, true); renderTask(task);
    }
    async function poll(task) {
        if (!tasks.has(task.id)) return;
        if (new Date(task.expires) <= new Date()) { expire(task); return; }
        try {
            await acceptJob(task, await api('ArchiveStatus', { id: task.job }));
        } catch (error) {
            if (!tasks.has(task.id)) return;
            if ([401, 403, 404, 410].includes(error.status)) {
                task.state = error.message; task.busy = false; task.terminal = true;
                await store(task, true);
            } else {
                task.state = 'ZIP status temporarily unavailable; retrying automatically.';
                task.timer = setTimeout(() => poll(task), error.status === 429 ? 30000 : 10000);
            }
            renderTask(task);
        }
    }
    function startFile(entry) {
        const task = newTask({ name: entry.name, total: entry.bytes, resource: { path: entry.path, version: entry.version }, state: 'Ready to save' });
        save(task);
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
            task.terminal = true; await store(task, true);
        } catch (error) { task.state = error.message; }
        task.busy = false; renderTask(task);
    }
    async function save(task) {
        if (task.busy) return;
        if (!window.showSaveFilePicker || !window.isSecureContext) { await browserDownload(task); return; }
        task.busy = true; task.state = 'Choose a save location…'; renderTask(task);
        try {
            // Invoke the picker before asynchronous work so browser user activation is retained.
            task.handle ||= await window.showSaveFilePicker({ suggestedName: task.name });
            if (await task.handle.requestPermission({ mode: 'readwrite' }) !== 'granted')
                throw new Error('Save permission was revoked. Choose a new file or use browser download.');
            if (!tasks.has(task.id) || task.terminal) return;
            let grant = validateGrant(await api('Grant', task.resource, true), task);
            if (!tasks.has(task.id) || task.terminal) return;
            task.version = grant.version; task.total = grant.bytes; task.expires ||= grant.expiresUtc;
            const file = await task.handle.getFile();
            if (file.size < task.offset) throw new Error('The saved file is shorter than expected. Restart save in a new file.');
            task.abort = new AbortController();
            task.state = 'Saving to disk'; renderTask(task);
            const chunkSize = 4 * 1024 * 1024;
            while (task.offset < task.total || task.total === 0) {
                task.abort.signal.throwIfAborted();
                if (new Date(grant.expiresUtc) - Date.now() < 30000)
                    grant = validateGrant(await api('Grant', task.resource, true), task);
                const end = Math.min(task.total - 1, task.offset + chunkSize - 1);
                let response;
                if (task.total > 0) {
                    response = await fetch(grant.url, {
                        credentials: 'omit', referrerPolicy: 'no-referrer', cache: 'no-store', redirect: 'error',
                        headers: { Range: `bytes=${task.offset}-${end}`, 'If-Match': `"${task.version}"` },
                        signal: task.abort.signal
                    });
                    if (response.status === 401 || response.status === 403) {
                        // Renew only once on resume/user retry; never append an unvalidated response.
                        throw new Error('The download link expired or access ended. Resume save to request a new link.');
                    }
                    const expectedRange = `bytes ${task.offset}-${end}/${task.total}`;
                    if (response.status !== 206 || response.headers.get('Content-Range') !== expectedRange ||
                        response.headers.get('ETag') !== `"${task.version}"` || !response.body)
                        throw new Error('The server could not safely resume this version. Refresh files or restart in a new file.');
                }
                const writer = await task.handle.createWritable({ keepExistingData: true });
                let received = 0;
                try {
                    await writer.truncate(task.offset);
                    await writer.seek(task.offset);
                    if (response) {
                        const reader = response.body.getReader();
                        try {
                            while (true) {
                                const { value, done } = await reader.read();
                                if (done) break;
                                received += value.byteLength;
                                if (received > end - task.offset + 1) throw new Error('Invalid download range length.');
                                await writer.write(value);
                            }
                        } finally { await reader.cancel(); reader.releaseLock(); }
                        if (received !== end - task.offset + 1) throw new Error('Download interrupted. Resume save to continue.');
                    }
                    task.abort.signal.throwIfAborted();
                    await writer.close();
                } catch (error) {
                    await writer.abort().catch(() => {});
                    throw error;
                }
                task.offset += received;
                if (tasks.has(task.id)) await store(task);
                renderTask(task);
                if (task.offset === task.total) break;
            }
            task.state = 'Saved to disk'; task.terminal = true; await store(task, true);
        } catch (error) {
            if (!task.terminal) task.state = error.name === 'AbortError' ? 'Download paused. Resume save to continue.' : error.message;
            if (tasks.has(task.id) && task.handle && !task.terminal) await store(task);
        } finally {
            task.busy = false; task.abort = null; renderTask(task);
        }
    }
    async function restore() {
        const db = await database;
        if (!db) return;
        const records = await new Promise(resolve => {
            const request = db.transaction('tasks').objectStore('tasks').getAll();
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => resolve([]);
        });
        for (const record of records) {
            if (record.owner !== member || (record.job && new Date(record.expires) <= new Date())) {
                await store(record, true); continue;
            }
            const task = newTask({ ...record, state: record.handle ? 'Download paused. Resume save to continue.' : 'Restoring ZIP…' });
            if (record.job && !record.handle) await poll(task);
        }
    }
    window.addEventListener('pagehide', () => tasks.forEach(task => { clearTimeout(task.timer); task.abort?.abort(); }));
    load();
    restore();
})();
