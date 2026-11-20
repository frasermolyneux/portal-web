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

For download writes, fetch `/api/integration/v1/session` with credentials and send
its `requestVerificationToken` in the returned `headerName` together with the
antiforgery cookie. The token is bound to the current session; fetch a new one
after signing in or out. ZIP creation and status use the existing authenticated
Portal identity. File grants retain the existing anonymous behavior. The versioned
download routes use the same request and response types as the
[native map download endpoints](native-map-downloads.md); capability URLs are
short-lived and must not be logged or persisted.

The aggregate statistics endpoint requires Dashboard.Read. Its 24/48-hour
figures count tracked game-player records by last-seen time, not distinct people;
the online total and per-game counts come from separate dependency snapshots.

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
