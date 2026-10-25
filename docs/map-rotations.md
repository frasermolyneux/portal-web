# Map rotations

Open **Map Rotations**, choose a rotation, then **Manage** beside its server and
assignment. This server page is the single place to run actions and review activity.
Edit the map list and order on the rotation page.

## Everyday actions

| Action | Use it to |
| --- | --- |
| **Apply rotation** | Repair files, then apply the selected configuration. |
| **Verify files** | Check required files are present and correctly sized, without changing configuration. |
| **More actions > Repair files** | Fix missing or incomplete files without changing configuration. |
| **More actions > Reapply configuration** | Update this assignment's configuration targets. |
| **More actions > Deactivate** | Clear this assignment's linked rotation configuration. |
| **More actions > Unassign** | Remove an inactive assignment, keeping map files and the rotation library. |

## Assigning a server

Choose the server and its normal rotation config file. Select **Also update AACP
map picker** and supply the **AACP config file** to maintain `scr_aacp_maps_1`
alongside `sv_maprotation`. The option belongs to this server assignment, not to
the rotation library. Both configuration targets are saved atomically.

A matching normal/map-picker pair is shown as one server row and one Manage
page, only when both rows have the same rotation ID and server ID. All actions
operate on this pair with one request. Files are shared: Apply/Repair do not
transfer the same map once per configuration. Each target's configuration status
and errors remain visible. Bookmarks to the child target lead to the same hub.

To disable AACP, **Deactivate** first and wait for successful completion, then
uncheck the option in **Edit settings**. Unchecking does not clear active server
configuration; the request is rejected instead. Unassign requires all targets
to be inactive with no work in progress and retains server map files.

**Advanced** options retain standalone/custom slots (including small, medium,
large and other AACP variables). These, different rotations, and removed history
remain separate; matching names never combine rotations.
Ambiguous duplicate standard targets are inspectable but block new actions until
an administrator resolves them.

Actions do not restart game servers. A deliberately required restart is a
separate Server Admin operation.

## Reading results

**Files** and **Configuration** are separate results. **Update needed** means the
rotation changed since that part was last applied; internal version counters
remain in use but are not shown. Verification establishes presence and size,
not checksums, successful game loading, or the running server's current values.

Activity includes both targets' histories, opens while work is running and collapses to the latest result
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
