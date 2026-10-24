# Native map downloads

`/MapDownloads` provides live files, recursive map manifests, map/mod collections,
individual saves, and ZIP preparation without leaving Portal. COD4x uses COD4;
COD5 and COD2 are supported. COD2 maps use the collection's plain files rather
than treating `main` as an archive folder. Only supported games receive native
links; existing static links, voting, map management and SFTP remain unchanged.
The standalone **Open in Maps Manager** link remains available.

## Trust and deployment

Configure `RedirectApi:BaseUrl` and `RedirectApi:ApplicationAudience` in Portal's
App Configuration namespace. Both are required at startup. Portal uses the
published typed Entra client with a request-local `ForPortal` read-only facade.
The existing authenticated `XtremeIdiotsId` claim is the forum member ID
(`XtremeIdiotsAuth` creates `IdentityUser.Id` from the verified forum identity).
Missing/invalid authenticated forum IDs fail closed. Anonymous callers have no
member ID. The connection address (normalizing mapped IPv4) and capped 512-character
User-Agent supply the fingerprint; browser-supplied member/IP fields are ignored.
No Portal role grants Maps administration privileges.

Maps must allowlist both Portal application identities and the exact browser
origins for direct-download CORS. Forwarded-header trust at the existing Portal
ingress must be correctly configured for `Connection.RemoteIpAddress` to be
authoritative. Coordinate API/identity readiness before merging or deploying.

All grant/archive creation POSTs require antiforgery, including anonymous grants.
ZIP creation/status require the existing Portal login, not another OAuth session.
The Maps API owns archive isolation and validates paths; Portal additionally
rejects empty, duplicate or over-15-folder selections. Errors retain HTTP status
without exposing private upstream messages. ZIP metadata never exposes the
legacy authenticated archive URL. The action manifest intentionally adds one
browser page, four read-only HTTP endpoints and two protected POST surfaces.

## Transfer and recovery

Only metadata, archive status and grant creation pass through Portal. Bytes use
short-lived Maps capability URLs directly; fetch omits credentials and referrers.
Portal does not proxy, buffer or persist download bodies or capability URLs.
The native page disables browser analytics/Application Insights automatic
tracking so capability URLs cannot appear in dependency/link telemetry.

Where File System Access is available in a secure context, a user-selected file
is written with bounded 4 MiB range responses streamed to disk. Each response must
be `206`, with the exact expected `Content-Range` and strong version ETag.
Offsets advance only after a successfully closed write; incomplete writes abort.
A disk checkpoint groups geometrically growing batches of those bounded ranges,
so repeatedly copying the committed prefix is linear rather than quadratic in
file size. An interruption discards the current uncommitted checkpoint and
resumes from the last closed checkpoint, whose offset is displayed and persisted.
A Web Lock permits one Portal disk save at a time across tabs. Resume reconciles
the latest persisted checkpoint while holding that lock; another tab's active or
completed save cannot be replayed against stale offsets. Browsers without Web
Locks use normal browser-managed downloads instead.
Each committed block also records SHA-256 integrity. Resume verifies the entire
committed prefix through bounded, at-most-4-MiB disk reads before appending; a
replaced or edited file requires a new save. Selecting the same destination for
another Portal transfer invalidates earlier checkpoints. Concurrent ZIP status
updates merge job metadata transactionally, preserving handles, hashes and offsets.
Polling updates cannot recreate a record completed or dismissed in another tab.
A longer partially saved file is truncated to the last committed offset on resume;
a shorter file requires an explicit restart in a new file. Changed versions are
never appended, even when renewing grants. Revoked permissions and session or
job expiry leave visible recovery messages.

IndexedDB stores resource identifiers, owner, file handle, version and committed
offset, never capability URLs. Resume requires a user gesture and a newly
authorized grant. Storage-policy failures degrade to current-page recovery.
Another login clears the previous owner's saved tasks when opening this page.
ZIP jobs automatically poll queued/running states, stop on ownership failures,
expire visibly, and back off on rate limits/transient failures. Dismiss removes
local tracking only, not server work. Archive submission is never blindly retried.
Paused ZIP expiry tracking is restored after reload; returning from the browser's
back/forward cache restarts suspended tracking without duplicate status requests.
Completed saves are never replaced by a later ZIP-expiry notification.

Browsers without File System Access use their normal download manager through a
direct link, without constructing an in-memory Blob. Browser-managed downloads
have no Portal-side byte progress/resume guarantee; inspect the browser's download
manager. **Use browser download** is also available on resumable-save tasks.

Focused tests cover controller caller isolation, antiforgery, folder bounds,
ownership failures, native anonymous/member journeys, automatic ZIP polling and
range-to-disk recovery. The disk browser fixture uses real OPFS writes and real
IndexedDB metadata; its synthetic picker handle is reattached on reload to avoid
a Windows headless Chromium OPFS-handle deserialization crash. This is not a
second application persistence implementation.

## Coordinated live verification

After the Maps backend image is ready and this Portal change has been approved
for deployment, use these native entry points (no Maps login is required):

| Environment | Native downloads | COD2 plain-file maps |
| --- | --- | --- |
| Production | https://portal.xtremeidiots.com/MapDownloads | https://portal.xtremeidiots.com/MapDownloads?game=cod2 |
| Development | https://app-portal-web-dev-swedencentral-f32b072841b1.azurewebsites.net/MapDownloads | https://app-portal-web-dev-swedencentral-f32b072841b1.azurewebsites.net/MapDownloads?game=cod2 |

COD5 collections use `?game=cod5`; COD4/COD4x use `?game=cod4`. Switch the
**Collection** dropdown to **Mods** to browse mod folders for the selected game.
The production default hostname is also an allowed browser origin:
https://app-portal-web-prd-uksouth-fe584e15256a.azurewebsites.net/MapDownloads.
SCM hostnames are not allowed origins.

1. In a signed-out/private browser, follow **Map and mod downloads** from `/Maps`.
   Browse a COD4/COD5 map and a mod folder, then COD2 plain-file maps. Verify live
   file sizes, the secondary standalone link, and an individual file download.
   ZIP selection must require Portal login; metadata and files must not.
2. In Chromium, select a save location directly from **Save file**. Pause a large
   transfer, reload, and use **Resume save** with permission granted. Confirm it
   resumes at a committed offset. In Firefox or another browser without the
   picker, confirm the normal browser download manager receives the file.
3. Sign in once to Portal. From Maps Index and a supported server's
   `/MapManager/Manage` page, follow the native entry point. Create a folder ZIP
   and a selection ZIP across pages/collections. Verify the 15-folder bound,
   automatic queued/running/ready updates, download, clear-selection and dismiss
   controls. There is no bulk-select or check-status button.
4. Change Portal users or sign out and reload Downloads. Previous-owner tasks
   must disappear; another user's ZIP must remain unavailable. No browser field
   supplies the forum member ID or client IP. Never copy real grant URLs into
   tickets, traces, screenshots, logs or persisted browser records.

Do not merge or deploy Portal merely because CI/review is green: the coordinating
owner must first confirm Maps backend image readiness and release approval.
