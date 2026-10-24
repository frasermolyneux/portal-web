# Portal-only map rotation recovery

These steps use the deployed Portal and its authorized services only. Do not
connect directly to game servers or the production database, manually upload
files, or restart servers as part of this file recovery.

## Before starting

Deploy the coordinated Servers Integration authenticated manifest/file-size repair
changes, Repository lifecycle/90-day history changes, and Sync V2 Apply and
cooperative-cancellation changes before using the updated Portal. **Apply rotation**
calls `POST /api/map-rotations/apply/{assignmentId}`. Deploying Portal alone does
not provide this service. Use the normal Sync deployment that imports the updated
OpenAPI definition into APIM; an app-only `maps-bootstrap` deployment does not
publish the new Apply route.

No production SQL reset or migration is needed to recover the nine observed
assignments. Leave their correct configuration values alone. Before the first
post-deployment repair, let pre-deployment legacy rotation/map-push work finish.
The service's fail-closed rollout gate can block new work even when the legacy
operation is on another server. Legacy histories remain replayable, but cannot
retroactively join the new V2 server lock. Failed/Terminated legacy status alone
does not prove its scheduled activities have stopped: unexplained outstanding
activities require service-side investigation. Do not hard-cancel running legacy
work, connect directly to servers, or manufacture healthy SQL/state badges.
Never infer safe cancellation from an acknowledgment.

Sign in with deployment permission for the relevant server. In **Map Rotations**,
open the currently assigned rotation, then **Status** beside the correct server
and configuration target. Record the rotation version, file deployment,
configuration activation, config file and variable, and latest operation/error.
Version badges alone are not evidence that files are present and non-empty.

## BlackOpsDM: restore files without rewriting configuration

1. Open the current **BlackOpsDM** assignment's **Status** page. Confirm the
   target server/configuration; do not create a replacement assignment.
2. Select **Repair files**. This queues file repair only. Wait for the recorded
   operation result, not merely the “queued” alert.
3. Inspect its per-map errors, especially **mp_akushima** and **mp_anbinh**.
   The investigation found four empty files in each map; source files were
   available. If any transfer/manifest error remains, stop and resolve it through
   the service owners rather than treating the version badge as success.
4. Select **Verify files**, wait for its result and confirm both maps pass the
   authenticated manifest's expected file-presence and size checks.
5. Confirm configuration/activation did not change. Do **not** use Reapply
   configuration, Deactivate, Unassign or a restart solely to restore these files.

## Knife: restore the missing file

Repeat the same **Status → Repair files → wait for results → Verify files → wait
for results** sequence on the current **Knife** assignment. Check **mp_a3_day**,
particularly the previously missing **mp_a3_day.ff**, and all other manifest
entries. Configuration should remain unchanged. No configuration rewrite,
deactivation, unassignment, or restart is required solely for restoring files.

## Validate all five servers and all nine assignments

The investigation baseline was 95 rotations, nine assignments across five servers,
with nine configuration variables across seven files matching the stored
assignments. Reconfirm the current assignments before acting; this is not a live
health assertion.

| Server | Portal validation |
| --- | --- |
| BlackOpsDM | Repair then verify as above; review every failed/partial result. |
| Knife | Repair then verify as above; check `mp_a3_day.ff`. |
| Sniper | Verify files on its current assignment; the investigated 21 October rotation had 10 maps / 34 files and needed no repair. |
| DM3 | Verify each current target separately, preserving the independent small/medium/large AACP slots and main target. The investigated 21 October main rotation had 37 maps / 144 files and needed no repair. |
| Ace | Investigate the current authenticated manifest first, as below; do not blindly Apply or Repair questionable nested entries. |

For every assignment compare **Files / Deployment** and **Configuration /
Activation**, deployed/activated versions, target file and variable, and recent
operation errors separately. Do not consolidate the DM3 slots or delete the old
rotation library. Successful verification checks the manifest's file presence and
sizes; it is not a checksum, game-load, current-map, or runtime-rotation test.

### Ace manifest investigation first

The investigated 12 October Ace rotation had three valid native `mp_a3` files,
but its manifest also included three questionable nested `mp_a/*` entries.
Open **Map and mod downloads**, select the server's game and `mp_a3`, and inspect
the current authenticated file listing/path/size information (use **Open in Maps
Manager** for the authorized source detail when needed). Record the exact nested
paths and expected sizes and have the manifest/source owner confirm whether those
entries belong to this map. Do not assume they are required because an old
manifest listed them. If the listing is unavailable or ambiguous, stop; do not
blindly apply nested files. Once the authenticated source is confirmed/corrected,
use **Verify files** and repair only if its results require it.

## Applying a deliberate rotation/configuration change

Use **Apply rotation**: “Repair files and apply this slot and its linked main/AACP
slot using the same rotation.” Only `sv_maprotation` and `scr_aacp_maps_1` on the
same server **and same rotation** are linked. Independent DM3 small/medium/large
slots and different rotations are never grouped. Each target records its own
operation evidence; a linked-target failure makes the overall Apply fail.

Review both file and configuration results for each target. Files are
repaired/synced before configuration is applied; failure is not a successful
handover. **Repair files**, **Verify files** and **Reapply configuration** operate
on the selected slot only, never automatically changing linked configuration, and remain
separate recovery actions even for synced, partial, failed or stale-active
assignments. Busy/removing/removed assignments cannot start another action.

No action automatically restarts a server. Only use an explicit, separately
authorized Server Admin restart if the game/mod actually requires one for a
deliberate configuration change. Configuration activation is not runtime proof.
Map transfers are staged on FTP and SFTP. SFTP configuration writes are staged
with restricted temporary-file permissions and preserve the original owner/group
and mode; FTP configuration writes use acknowledged in-place STOR to preserve
ownership and mode. V2 reads the exact variable back from the file after either
configuration write. RCON commands are acknowledged, but running values are not
independently queried.
Standard rotations over 1024 characters now fail explicitly rather than reporting
success through unreferenced suffix variables. Review that error instead of
assuming the full rotation was applied.

**Cancel** may report “Cancellation requested”; an in-flight transfer or config
activity must finish safely first. Keep watching progress. This acknowledgment is
not cancellation success, and does not reset database state. Sync alone records
and finalizes operation/assignment lifecycle state under its server lock. Portal
only submits requests, displays/polls outcomes and reloads repository data; it
does not PATCH lifecycle state after a terminal status or cancellation response.
Dispatch failures do not create optimistic persisted busy state.

Refresh **Status** to read Sync's recorded outcome. If the operation is terminal
but the assignment remains busy, or legacy activity completion is uncertain,
stop and request **service-side investigation**. There is no Portal reset or
finalization fallback. Legacy hard cancellation is rejected with HTTP 409; do not
bypass that refusal, force healthy badges, connect directly to servers or reset
SQL state. A Durable terminal badge alone is not proof that legacy activities
have drained.

Once Sync has finalized a non-busy result, uncertain configuration cancellation
requires successful **Deactivate** before **Unassign**. Unassign preserves server
map files and the rotation library; removed assignment history is retained for
90 days.

For linked main/AACP Apply, either target can request cancellation using its own
persisted in-progress operation; the server, rotation and exact linked-slot relationship are
verified before contacting Sync.
