# Forum integration

The versioned contract is described in [forum-integration-openapi.yaml](forum-integration-openapi.yaml).
Requests originate from `https://www.xtremeidiots.com` or `https://test.xtremeidiots.com`.
Use `credentials: "include"` for Portal sessions. Banner HTML is configured by Portal
administrators and should be treated as HTML, not plain text.

Use the no-store `GET /api/integration/v1/session` response to decide which
protected shortcuts to show. The `capabilities` object contains three booleans:

| Field | Portal destination | Required policies |
| --- | --- | --- |
| `players` | `/Players` | `Players.Read` |
| `serverAdmin` | `/ServerAdmin` | `GameServers.Admin.Read` |
| `chatLogs` | `/ServerAdmin/ChatLogIndex` | `GameServers.Admin.Read` and `ChatLog.Read` |

All three are false for guests. Show a shortcut only when its field is `true`;
missing fields do not grant access. These flags describe access to the landing
pages, not permission to read individual resources or perform mutations.

Moderation `actionUrl` is a Portal-relative link to the individual action's edit
page, or `null` if the caller cannot edit that action. The destination checks
authorization independently. Resolve it against the configured Portal origin.
Game icons use `/images/game-icons/{gameType}.png` (`Unknown.png` as fallback).
Use `/images/noimage.jpg` for a missing map image, `/MapDownloads` for the
download catalogue, and `/MapDownloads?rotation={gameServerId}` for a configured
server rotation's ZIP landing page.

`GET /api/integration/v1/server-admin` requires `GameServers.Admin.Read` and
returns a private, no-store `servers` array matching the Portal index (up to 50
agent-enabled servers). Each row contains `title` (plain text), `gameType`,
`hostname`, `queryPort`, nullable `currentPlayers`, `maxPlayers`, `currentMap`
and `mod`, plus Portal-relative `consoleUrl`, `chatLogUrl` and `manageMapsUrl`.
The optional links are null unless the destination policy permits them. The
Console and row policy uses the existing Server Admin see-all access model;
ChatLog and Manage Maps use their existing destination policies. Missing live
status for a server is represented by null fields. A failed required dependency
returns 503, not fabricated counts. Guests receive 401 and authenticated
callers without access receive 403.

The rotation download route may also show an observed-history pack when no
configured rotation exists. `/MapDownloads?game={gameType}` selects the game-wide
catalogue; it is not a server-specific collection. There is currently no public
server-collection filter.

`GET /api/integration/v1/player-map` is no-store. `points` contains coarse
five-degree cell centers and observation counts, grouped by game and activity,
from public servers only. A cell needs three identified observations (or live
slots for live cells) to display; unidentified recent rows cannot qualify a cell.
`players` contains names at the same coarse precision only for authenticated
callers with `Players.Read` for the corresponding game. Neither array contains
IP addresses or player identifiers.

The arrays are independent: an authorized caller may receive live `players`
even when no live `points` meet the public aggregation threshold. Consumers
should render permitted players independently rather than treating an empty
`points` array as an empty map. A live player without valid coordinates cannot
appear in either array.

The map uses online server snapshots and a sample of the latest 200 geolocated
observations requested from Repository within 48 hours, filtered to public
servers. `recentlySeen` does not mean offline: known player IDs are deduplicated
with live observations taking precedence, but unidentified rows cannot be
matched and may repeat or overlap. Counts are observations, not unique people.

For download writes, fetch `/api/integration/v1/session` with credentials and send
its `requestVerificationToken` in the returned `headerName` together with the
antiforgery cookie. The token is bound to the current session; fetch a new one
after signing in or out. ZIP creation and status use the existing authenticated
Portal identity. File grants retain the existing anonymous behavior. The versioned
download routes use the same request and response types as the
[native map download endpoints](native-map-downloads.md); capability URLs are
short-lived and must not be logged or persisted.

The aggregate statistics endpoint requires Dashboard.Read. Its 24/48-hour
figures sum Repository's per-game counts of tracked player records by last-seen
time, not distinct people. Each window uses the same UTC end time. The online
total and per-game online counts come from separate dependency snapshots.
Stats returns `503` if a required count is unavailable; it does not substitute zero.

The test OAuth provider is optional. Configure `XtremeIdiots:TestAuth:ClientId` and
`XtremeIdiots:TestAuth:ClientSecret` using the existing secure configuration system.
Its callback is `/signin-xtremeidiots-test`; production continues to use
`/signin-xtremeidiots`. Confirm the Portal host can reach the test forum's token and
user-info endpoints before enabling the provider.

A user first signs into Portal with their production forum account, then chooses
**Link test account** in their Portal profile and completes both OAuth challenges.
The test forum login is bound to the existing Portal Identity user, not to an
assumed matching numeric forum ID or email address. Unlinked test accounts cannot
sign in or create Portal profiles.

Test forum database refreshes disable OAuth clients and clear their credentials and
tokens. Register a **new client ID** after each refresh; never restore or re-enable
the previous client ID on a refreshed database. Test login links are scoped to the
issuer, client ID and subject, so a new client ID requires users to link again.
Coordinate test provider activation with the test forum workstream.
