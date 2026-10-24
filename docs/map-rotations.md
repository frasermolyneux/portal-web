# Map rotations

Open **Map Rotations**, choose a rotation, then **Manage** beside its server and
slot. This server page is the single place to run actions and review activity.
Edit the map list and order on the rotation page.

## Everyday actions

| Action | Use it to |
| --- | --- |
| **Apply rotation** | Repair files, then apply the selected configuration. |
| **Verify files** | Check required files are present and correctly sized, without changing configuration. |
| **More actions > Repair files** | Fix missing or incomplete files without changing configuration. |
| **More actions > Reapply configuration** | Update only this slot's configuration. |
| **More actions > Deactivate** | Clear this slot's rotation configuration. |
| **More actions > Unassign** | Remove an inactive assignment, keeping map files and the rotation library. |

Apply includes a matching main rotation/map-picker pair on the same server using
the same rotation (`sv_maprotation` and `scr_aacp_maps_1`). Independent
small/medium/large slots and different rotations remain separate. Other actions
operate on the selected slot only.

Actions do not restart game servers. A deliberately required restart is a
separate Server Admin operation.

## Reading results

**Files** and **Configuration** are separate results. **Update needed** means the
rotation changed since that part was last applied; internal version counters
remain in use but are not shown. Verification establishes presence and size,
not checksums, successful game loading, or the running server's current values.

Activity opens while work is running and collapses to the latest result
afterwards. Expand it for previous results, errors, and technical tracking details.
Positive messages such as already-verified files are not failures.

**Cancel** requests a safe stop after the current step. Wait for the recorded
result; an acknowledgement is not completed cancellation. If progress is
unavailable, refresh. If a finished action leaves the assignment busy, ask a
portal administrator to investigate rather than resetting state or unassigning.
Removed assignment history is retained for 90 days.

If verification unexpectedly requires unrelated files, inspect the source in
**Map and mod downloads / Maps Manager** before repairing. Correct the source,
not just its SQL catalogue copy, which is refreshed from the redirect.

## Maintenance boundaries

Sync owns operation and assignment lifecycle state under a server-wide lock.
Portal submits requests and reads results; it does not finalise or reset state.
Keep internal deployment/activation versions and replay-compatible legacy
orchestrations: hiding UI counters is not permission to remove those safeguards.
