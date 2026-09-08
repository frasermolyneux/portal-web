# UI Testing

The portal integration suite runs the ASP.NET Core application locally with deterministic test configuration, an in-memory SQLite identity database, test-only authentication profiles, and in-process fake API clients. Playwright connects to Kestrel on an operating-system assigned loopback port. No Azure login, Docker daemon, SQL Server, external API, or manually started web process is required.

## Run locally

### Prerequisites and bootstrap

Install:

- Git and a clone of this repository. Setup fetches complete history from
  `origin` when the checkout is shallow, because Nerdbank.GitVersioning needs
  earlier commits. This also handles coding-agent checkouts that override
  `fetch-depth: 0`; a fetch failure stops setup with an actionable error.
- The .NET SDK required by [global.json](../global.json), currently 10.0.400. Its
  `latestPatch` policy permits servicing patches within that feature band, not
  older SDKs such as 10.0.303.
- Node.js 22.x, selected by [.node-version](../.node-version), and npm >=10.
  Servicing updates within Node.js 22 are supported; npm package versions are
  fixed by the committed lockfile.
- PowerShell >=7.2 (`pwsh`), including on Linux.

Run the `dotnet: setup-tests` VS Code task, or:

```powershell
pwsh -NoProfile -File scripts/setup-test-environment.ps1
```

The bootstrap validates tools before installation, runs `npm ci --include=dev`,
builds the solution in Release, and installs Chromium using the generated
`playwright.ps1` from the .NET integration-test package. On Linux it includes
`--with-deps`; system dependency installation requires root or sudo. It then
runs the existing login-page test and requires a TRX containing exactly one
passing test. A zero-test or skipped result is a setup failure.

No global SDK, Node.js, or PowerShell installation is attempted by this script.
It can be called from outside the repository, restores the caller's directory,
and can be rerun after changing branches or dependencies. NuGet and Playwright
reuse their normal user caches; npm reconstructs `node_modules` from the lockfile.
Do not run two builds/bootstrap processes in the same checkout concurrently.

### Separate test suites

Use the same runner locally and in CI:

```powershell
pwsh -NoProfile -File scripts/run-tests.ps1 -Suite Unit
pwsh -NoProfile -File scripts/run-tests.ps1 -Suite HttpIntegration
pwsh -NoProfile -File scripts/run-tests.ps1 -Suite Browser
```

| Suite | VS Code task | Includes | Browser installation |
| --- | --- | --- | --- |
| `Unit` | `dotnet: test` | Unit/controller tests in the unit project | Never |
| `HttpIntegration` | `dotnet: test-http` | TestServer, authorization matrix, manifest, health and endpoint tests | Never |
| `Browser` | `dotnet: test-browser` | All Playwright tests and browser-backed Reqnroll workflows | Matching Chromium and smoke check |

Commands default to Release and build only the selected test project and its
dependencies. The shared prerequisite check runs for every suite, but unit and
HTTP tests do not require installed browsers or execute the browser smoke test.
Use the full bootstrap only when you want to prepare the browser environment too.

```powershell
# Focus an existing workflow category without leaving the browser suite.
pwsh -NoProfile -File scripts/run-tests.ps1 -Suite Browser -Filter "Category=server-feed" -NoBuild

# Validate runtime-compiled Razor locally, or select a single HTTP test.
pwsh -NoProfile -File scripts/run-tests.ps1 -Suite HttpIntegration -Configuration Debug -Filter "FullyQualifiedName~PageSmoke"
```

`-Filter` is ANDed with the suite category, including when it contains OR
expressions; it cannot pull tests from another suite. `-NoBuild` explicitly
reuses the chosen configuration's outputs and fails if they are missing. It does
not detect stale builds: omit it after changing code or categories.

Each selection must produce a fresh TRX with at least one executed, passing test.
A typo yielding zero tests, an all-skipped selection, missing report, or failed
test returns a failure, not a successful empty run. The console reports total,
executed, passed and skipped counts. Results are kept in
`src/TestResults/<Suite>/<run-id>/<Suite>.trx`.

`-Suite Integration` runs HTTP then browser suites; `-Suite All` also runs unit
tests. Every selected suite must match tests. For domain-specific filters, select
the owning suite instead of `All` or `Integration`. The existing
`dotnet: test-integration` task and `run-ui-tests.ps1` remain compatibility
entry points for both integration suites; `-SkipBuild` maps to `-NoBuild`.

### Browser ownership and execution limits

The browser harness reuses an assembly-owned Playwright/Chromium instance instead
of launching Chromium for each test. Every ordinary test still gets an isolated
browser context, Kestrel host, SQLite database, authentication headers and scenario
mocks. Sharing the browser process does not share application state. The
navigation collection can retain its shared read-only host, but its role contexts
are isolated and participate in the same context limit.

Browser contexts are acquired through bounded leases and returned even when
initialization or disposal fails. A test disposes its context and host, never the
assembly's browser. Assembly teardown closes the shared resources. Unexpected
browser disconnection is a visible failure, not an implicit test retry.
The defaults are two active contexts, a 120-second lease wait, a 60-second native
browser launch, 30-second action/navigation waits, and the existing five-second
retrying assertion timeout. Application hosts have a 60-second startup budget and
a 30-second shutdown budget. A failed native context close blocks further
allocations rather than exceeding the limit with an unclosed context.

The Browser command and bootstrap smoke both use `scripts/browser.runsettings`:

- At most two xUnit collection workers, using the conservative scheduler.
- A three-minute VSTest hang watchdog and fifteen-minute test-session limit.
- No automatic test reruns and no memory dumps; the blame sequence XML is retained
  with the existing test artifact when a test host hangs.
- The CI Browser job has a twenty-minute outer limit, including setup/build.

Unit and HTTP commands do not use these browser runner settings, and the browser
runtime stays lazy for non-browser execution. Existing Reqnroll workflow
serialization remains in place. Use the repository commands/VS Code tasks for
the complete configuration; a raw `dotnet test` invocation must explicitly pass
`--settings scripts/browser.runsettings` to get the runner watchdog.

### Measurement baseline, before targets

The normal local test commands remain uninstrumented for quick iteration. Opt
into reproducible evidence with:

```powershell
pwsh -NoProfile -File scripts/run-tests.ps1 -Suite Unit -Coverage
pwsh -NoProfile -File scripts/run-tests.ps1 -Suite HttpIntegration -Coverage
pwsh -NoProfile -File scripts/run-tests.ps1 -Suite Browser -Measure
```

The matching VS Code tasks are `dotnet: coverage-unit`, `dotnet: coverage-http`,
and `dotnet: measure-browser`. `-Coverage` implies `-Measure`; with `-Suite All`
or `Integration`, coverage applies only to the Unit/HTTP portions. Explicit
`-Suite Browser -Coverage` is rejected rather than pretending UI coverage was
collected. Bootstrap smoke is not included in the measurement baseline.

CI enables measurement for all three suites and coverage for Unit/HTTP. The
existing Actions summaries and single PR comment show the measurements beside
the test results; there are no new coverage-status checks or percentage gates.
Raw Cobertura, discovery output, and `measurement.json` are included in the
existing suite artifact, not uploaded through a competing reporting system.

#### What the coverage numbers mean

`scripts/coverage.runsettings` defines the denominator:

- Instrument only `XtremeIdiots.Portal.Web` and
  `XtremeIdiots.Portal.Integrations.Forums`.
- Exclude test assemblies, third-party assemblies, `**/obj/**` source paths,
  and code marked `GeneratedCodeAttribute` or `ExcludeFromCodeCoverageAttribute`.
- Do not blanket-exclude `CompilerGeneratedAttribute`: doing so would discard
  real asynchronous application execution. Auto-properties remain included.

Reports show covered/coverable lines and branches, with percentages calculated
from those counts. Unit and HTTP coverage overlap and are reported separately:
**do not add or average the two percentages**. No combined coverage is claimed.
A zero branch denominator means not applicable, not 0% or 100%.

Headline branch coverage uses the collector's all-branch totals. Coverlet can
include branches without source-line sequence points in those totals; the
expandable module breakdown is explicitly labeled **source-mapped branches**.
Those module branch counts need not sum to the all-branch denominator. Do not
derive missing module counts from rounded percentages.

These are .NET instrumentation measurements, including coverable Razor/.NET code
that is not excluded by the profile. They are not JavaScript coverage, visual
coverage, or proof that an endpoint works in a browser. Browser tests deliberately
remain uninstrumented. Missing or malformed evidence is unavailable/invalid,
never silently reported as zero coverage.

#### Provenance and timing

Each measured invocation retains its selection, configuration, framework, SDK,
OS/architecture, source revision, working-tree status, test/production assembly
hashes, collector version and coverage-profile hash. The metadata distinguishes
a full suite from a filtered run and a fresh build from `-NoBuild`/reused output.
Dirty, filtered, or reused-build measurements are useful for investigation but
must not be mistaken for a clean, comparable full-suite baseline.

Discovery runs through VSTest in a controlled English CLI locale and retains the
raw list. Discovered cases and execution results are distinct observations:
dynamic theories can expand differently during execution. Zero discoveries fail
explicitly; an unfamiliar discovery-output format fails rather than inventing
a count.

Discovery time, test-process time, and the existing TRX time window are labeled
separately. Process time includes collector/test-host overhead, not the earlier
build, npm/bootstrap, or complete CI job. Coverage instrumentation changes timing,
and parallel suite durations must not be added as if they were wall-clock runtime.

This establishes current-run baseline evidence. It does not infer a flaky-test
rate from one run, classify a rerun as a flake, or invent a historical/base-branch
delta. Repeatability and performance comparisons need the same suite, selection,
binaries/profile and runtime conditions. For future coverage comparisons across
revisions, review changes in scope and denominators; hashes identify the different
inputs rather than proving that different revisions are equivalent. Coverage
records execution, not assertion quality. The existing
Code Quality workflow is independent; these measurements do not add a SonarCloud
coverage gate.

### CI and remote environments

- PR and deployment verification use separate **Unit tests**, **HTTP integration
  tests**, and **Browser tests** jobs in the reusable `test-and-publish.yml`.
  Each has its own results artifact and builds on an isolated runner, avoiding
  cross-job build paths and browser executable permissions. Shared npm/NuGet
  caches remain enabled.
- Draft PRs, including coding-agent PRs, run unit and HTTP checks. Browser tests
  and publishing run when ready for review; deployment conditions still exclude
  drafts. Full runs must pass all three suites and the aggregate test gate before
  publishing the deployable web artifact.
- PR verification retains the required `build-and-test` status as an aggregate
  over the reusable workflow, including publishing when enabled. It fails if the
  workflow fails, is cancelled, or is skipped, and forwards the published version
  to existing deployment jobs. Repository rules do not need to be relaxed.
- The separate Code Quality workflow retains its existing shared analysis build
  and unit-test invocation. It is not the browser/HTTP execution gate.
- Copilot setup checks out the repository, installs the declared runtimes, and
  completes the full bootstrap through the smoke check before the agent starts.
  It does not run the full integration suite on startup.
- The Ubuntu 24.04 devcontainer installs the pinned SDK, Node.js 22, and
  PowerShell, then runs the full bootstrap in `postCreateCommand`. Rebuild an
  existing container to pick up the new toolchain. Keep its SDK/Node feature
  versions aligned when updating `global.json` or `.node-version`.
- Bootstrap results use fresh directories under `src/TestResults/bootstrap/`.
  CI uploads these with the browser results; Copilot setup uploads
  them separately. Retention is seven days.

### Troubleshooting setup

- **SDK not found:** install the version requested by `global.json`, reopen the
  terminal, and check `dotnet --version` from the repository root. Do not edit the
  pin to bypass the failure.
- **Shallow versioning history:** allow read access to `origin` so setup can
  fetch the missing commits and tags. An offline shallow clone cannot calculate
  the application's version; use a full clone rather than overriding version
  metadata or ignoring the build failure.
- **Node/npm mismatch:** select Node.js 22 and its bundled npm, then rerun setup.
- **npm lock mismatch:** use `npm install` in the web project only for an
  intentional dependency update and commit both manifests. Routine setup must
  use `npm ci`, not silently rewrite the lock.
- **Missing Release outputs:** run the default bootstrap, not `-Phase Browser`.
- **Browser download or Linux library failure:** check network/proxy access to
  NuGet, npm, the Playwright download hosts, and Ubuntu package repositories;
  allow sudo for Linux dependencies. Prepare these before the coding-agent
  firewall is applied. Never disable network controls to hide setup failures.
- **Chromium smoke failure:** inspect the console output and
  `src/TestResults/bootstrap/<run-id>/bootstrap.trx`. Installing binaries alone
  does not prove the browser can launch and load the application.

## Test structure

- `Hosting/` builds isolated TestServer and Kestrel application hosts from the production `PortalWebApplication` composition.
- `Authentication/` defines named role profiles delivered through the test-only `X-Portal-Test-Profile` header.
- `Authorization/` contains the executable policy matrix for all 53 policies and five baseline roles.
- `Health/` verifies required liveness, readiness, and version endpoints.
- `Manifest/` discovers and classifies every MVC/API action and enforces the approved application surface.
- `Playwright/` verifies rendered pages, browser behavior, policy-controlled UI, and direct authorization enforcement.
- `Workflows/` contains Reqnroll `.feature` specifications, domain scenarios, and Playwright step bindings for state-changing features.

Browser tests reject unexpected external requests and fail on same-origin request failures, HTTP error responses, console errors, and page errors. Known cosmetic CDN styles are omitted in the isolated environment.

## Adding coverage

Use HTTP integration tests for broad routing, endpoint, and Razor rendering coverage. Use Playwright when the behavior depends on browser rendering, JavaScript, navigation visibility, or a complete user workflow.

Unit tests inherit `[assembly: AssemblyTrait("Category", "Unit")]`. Handwritten
integration classes must declare exactly one `[Trait("Category", "HttpIntegration")]`
or `[Trait("Category", "Browser")]`. Browser-backed `.feature` files must carry
`@Browser` as well as their existing domain/workflow tags; their generated classes
are not located in the Playwright namespace. Never classify by namespace alone.
`TestCategoryContractTests` checks the compiled handwritten and generated tests
for missing or conflicting suite categories so new tests cannot silently fall
between the HTTP and browser selections.

Prefer accessible selectors by role, label, and visible text. Add `data-testid` only when the control has no stable accessible selector. Razor changes must follow `docs/ui-standards-guide.md`.

Authorization tests must keep real policies and handlers active. Add test identities or scenario data through the integration project rather than adding production test-login endpoints or credentials.

### Synchronization conventions

- Use retrying `Assertions.Expect` for DOM visibility, presence/count, text,
  values and attributes. Keep presence checks distinct from visibility checks:
  an unauthorized field should be absent when the policy removes its markup.
- Register response waits before the triggering action, match the HTTP method
  and exact path, and include identifying filters/search values where applicable.
  Response headers alone do not prove the browser has processed the body:
  wait for the resulting DOM state or the matching DataTables draw.
- Hold asynchronous fake operations with `RequestGate`, await their entry signal,
  assert the in-flight state, then explicitly release them. Release held work
  during cleanup too. Do not create a supposed in-flight window with a short sleep.
- Feed scenarios observe request starts and completion in the browser, and use
  explicit response gates for overlap/supersession. After the initial load they
  stop the periodic scheduler and drive the public refresh operation themselves;
  unrelated polling cannot consume planned responses midway through an assertion.
- Do not add blanket retries, `NetworkIdle` waits, or arbitrary sleeps to make a
  test green. Infinite waits in cancellation/watchdog regression probes and
  asynchronous error injection are intentional test stimuli, not synchronization.

## Diagnosing failures

### GitHub Actions and pull requests

Each suite writes a native Actions job summary with counts, duration, and bounded
failure details. Where the TRX supplies an existing repository-local test source
and line number, failures also appear as workflow error annotations. The
**Test results summary** job combines Unit, HTTP integration, and Browser results
in one table with links to the workflow run and existing suite artifacts.
Bootstrap smoke results are shown separately, never added to browser coverage.

Same-repository PRs receive one **Portal test results** comment from
`github-actions[bot]`. Later runs update that owned comment rather than creating
a stream of comments. It identifies the head/tested commits, run attempt, suite
outcomes and artifact links. A closed PR, changed head, or newer reported
run/attempt prevents a stale update. Fork and Dependabot PRs retain workflow
summaries and artifacts but do not receive a write-token comment from this
workflow.

Test jobs remain read-only. PR-comment permission is isolated in the
`Update PR test results` job; it does not run tests or download/execute artifacts.
The existing test checks and required `build-and-test` aggregate remain
authoritative. Missing, malformed, zero-test and all-skipped reports cannot turn
a failed or unstarted job green. A failing build/setup/upload is reported as a
job failure even when some test results are available.

The existing per-suite TRX artifacts now include their nested diagnostics with
the same seven-day retention. There is no second copy of the test-result upload
or competing test-check system. Reporting contracts can be checked locally with
`node --test .github/scripts/*.test.js`; this uses Node's built-in
test runner and the installed PowerShell, without another test dependency.

### Local evidence and traces

The TRX files remain the source of test results. Diagnostic files are kept beside
them under `src/TestResults/<Suite>/<run-id>/diagnostics/`; bootstrap smoke
diagnostics are isolated under the corresponding `bootstrap/<run-id>` directory.
Raw `dotnet test` execution uses the integration assembly's
`TestResults/diagnostics` directory unless `PORTAL_TEST_DIAGNOSTICS_DIRECTORY` is
set explicitly.

Browser failures retain a Playwright trace, a final screenshot when the page is
still available, browser console/page/network errors, and application log output.
The per-test metadata identifies the original test or scenario, including theory
arguments; unique directories prevent parallel tests from overwriting evidence.
Capture happens before browser disposal, and successful test evidence is
discarded. A browser that fails to launch or has already crashed may not produce
a screenshot or complete trace: the setup/capture error and available logs are
the evidence in that case, not a fabricated successful capture.

Download the existing suite artifact from the workflow run, extract it locally,
and open a retained trace with the generated Playwright CLI:

```powershell
pwsh src/XtremeIdiots.Portal.Web.IntegrationTests/bin/Release/net10.0/playwright.ps1 show-trace <path-to-trace.zip>
```

Keep traces and logs within the repository's artifact access boundary. They can
contain rendered form values, request data, and application messages. The normal
suite uses synthetic identities and fake backends; do not point this harness at
production services or put real credentials in test fixtures. Diagnostic capture
does not weaken existing browser-error assertions or turn failing tests into
successful runs.

To render a local summary from a single invocation:

```powershell
pwsh -NoProfile -File scripts/report-test-results.ps1 -Suite Browser -ResultsDirectory src/TestResults/Browser/<run-id> -RunOutcome failure
```

Point the reporter at one invocation, not a directory containing several old
runs: it deliberately refuses ambiguous results rather than combining stale
test counts.

## Authorization matrix

`AuthorizationMatrix` is the executable authorization specification. Every policy is tested through the real `IAuthorizationService` for Anonymous, Moderator, GameAdmin, HeadAdmin, and SeniorAdmin. Resource-sensitive policies add scenarios for ownership, action type, game/server scope, direct permissions, COD4/COD4x equivalence, and `PotentialAccessProbe`.

Adding an `AuthPolicies` constant without a registered policy and matrix entry fails the integration suite. System-only policies must be included in the explicit non-assignable list.

## Action manifest

`PortalActionManifest` reads ASP.NET Core's runtime `ControllerActionDescriptor` collection. The approved baseline currently contains 248 actions classified as browser pages, HTTP endpoints, state changes, downloads/streams, or external callbacks.

The readable baseline is committed as
`src/XtremeIdiots.Portal.Web.IntegrationTests/Manifest/portal-actions.approved.txt`.
Adding, removing, rerouting, or reclassifying an action fails the suite with the
actual removed (`-`) and added (`+`) entries, not just an opaque hash mismatch.
The failure retains approved, actual, and diff files under the run's diagnostics
directory. Reclassification appears as removal of the old classification and
addition of the new one.

Review the diff before updating the committed baseline and `ApprovedCounts`.
Never copy an actual snapshot over the approved file without checking that each
changed action and authorization boundary is intentional. The initial readable
snapshot was generated from and verified against the previously approved hash;
this reporting change does not approve new application endpoints.

Browser pages that require seeded identifiers or domain-specific fake responses are implemented as Phase 3 workflow scenarios. Deterministic view-only pages remain in the fast `PageSmokeIntegrationTests` set.

## Workflow packs

Phase 3 browser workflows are executable Gherkin specifications powered by Reqnroll and xUnit. Each `.feature` file owns readable Given/When/Then scenarios, while its `*Steps.cs` binding class performs Playwright interactions and assertions. Generated feature code is written below `obj/` and is not committed.

Each workflow scenario replaces only the dependencies owned by that test host and records state-changing client calls in thread-safe queues. Reqnroll creates binding instances per scenario, and an async `AfterScenario` hook disposes the context lease and Kestrel host. Chromium is owned by the assembly, not the scenario. Existing `@workflow` serialization through `reqnroll.json` is retained while ordinary browser collections use bounded parallelism. Steps assert the browser result and exact downstream DTO rather than sharing mutable global mocks.

Run a workflow pack independently through its feature tag:

```powershell
pwsh -NoProfile -File scripts/run-tests.ps1 -Suite Browser -Filter "Category=admin-actions"
pwsh -NoProfile -File scripts/run-tests.ps1 -Suite Browser -Filter "Category=tags"
pwsh -NoProfile -File scripts/run-tests.ps1 -Suite Browser -Filter "Category=game-servers"
pwsh -NoProfile -File scripts/run-tests.ps1 -Suite Browser -Filter "Category=say-command"
pwsh -NoProfile -File scripts/run-tests.ps1 -Suite Browser -Filter "Category=map-control"
pwsh -NoProfile -File scripts/run-tests.ps1 -Suite Browser -Filter "Category=player-moderation"
pwsh -NoProfile -File scripts/run-tests.ps1 -Suite Browser -Filter "Category=server-feed"
pwsh -NoProfile -File scripts/run-tests.ps1 -Suite Browser -Filter "Category=cod4x-lifecycle"
```

When adding a workflow, place its feature, bindings, and scenario fake together under `Workflows/<Domain>/`. Use scenario outlines for behavior permutations, keep technical setup out of feature wording, and use domain-specific step phrases because bindings are global within the Reqnroll project.

Current Pack B coverage includes:

- Admin Action creation for SeniorAdmin and Moderator roles, direct Ban denial, rich-text reason validation, repository command and notification payloads, success navigation, and repository failure behavior.
- Tag definition create, edit, and user-defined delete workflows for GameAdmin, plus direct create denial for Moderator.

Current Pack C coverage includes:

- RCON credential rotation, password visibility, blank-password preservation, server-side validation, scoped write denial, exact namespace/password orchestration, and configuration failure feedback. Production RCON JSON shape is covered by `NamespaceSettingsSerializerTests`.
- Game-server deletion success, SeniorAdmin-only direct access enforcement, exact delete commands, and repository failure feedback.
- FTP/SFTP transport switching, complete SFTP credential rotation, blank-secret preservation, fingerprint/path validation, scoped credential denial, password visibility, exact namespace/value orchestration, and repository failure feedback. Production FTP/SFTP JSON shapes are covered by `NamespaceSettingsSerializerTests`.

Current Pack D coverage includes:

- Live Say broadcasts for direct permission and GameAdmin access, exact trimmed RCON payloads, server-side validation, Moderator UI and forged-request denial, and backend failure feedback.
- Map loading, restart, fast restart, and next-map commands; the separate server-restart permission; direct-grant UI boundaries; forged restart denial; exact CoD4 RCON dispatch; and backend failure feedback.
- Connected-player Kick, TempBan, and Ban actions; independent direct Kick/Ban grants; Moderator Kick access; canonical live slot/GUID/name binding; forged identity, stale-slot, mismatched-search, and HTML-bearing-name defenses; exact CoD4 commands; admin-action persistence; and explicit RCON-success/persistence-failure feedback.
- Unified server feed rendering and HTML safety, event filtering, source-toggle cursor resets, pause/resume buffering, item deduplication, overrun notices, background-page suppression, overlap prevention, forced reload supersession, and disposal.
- CoD4x plugin install, rollback, and unload request contracts; runtime-state preservation; Linux artifact metadata; direct and role authorization; client/server validation; malformed/unavailable settings; and repository queue failures.

These workflows also guard degraded player-tag rendering, Development runtime compilation of the Admin Actions view component, and server-side validation of visible Summernote text. Other Phase 3 domain packs remain separate and can be added under `Workflows/<Domain>/` without changing the shared host.

## Bug-hunting playbook

Use the suite as an executable investigation tool rather than writing broad browser coverage first:

1. Start from the owning controller, Razor view, JavaScript module, authorization handler, and typed-client call. State one falsifiable behavior hypothesis.
2. Add the smallest Given/When/Then scenario that distinguishes the expected behavior from the suspected defect. Keep real application composition, middleware, antiforgery, model binding, policies, handlers, and serializers active.
3. Replace only external dependencies owned by the scenario. Record exact downstream DTOs and operation ordering in thread-safe queues. Use Playwright routing only when the browser behavior itself needs a controlled response sequence, such as polling races.
4. Cover both UI visibility and forged direct requests for authorization-sensitive actions. Role success alone is insufficient; include direct grants and mismatched game/server scopes where applicable.
5. Start response waits before the triggering click, match the exact HTTP method and route path, and use retrying `Assertions.Expect` checks for asynchronous DOM state. Do not use arbitrary sleeps as the primary synchronization mechanism.
6. Run the focused feature category while iterating. When a scenario exposes a production defect, fix the controlling path and retain the scenario as the regression specification.
7. Before completion, run the complete isolated suite, CI-equivalent unit/build checks, format verification, and the required `code-review` agent.

All `@workflow` scenarios remain serialized. A scenario owns one browser fixture and must not replace it without disposing the previous Kestrel host and browser context.

## Lessons from discovered defects

The current workflows have repeatedly found these defect classes:

- HTTP 200 responses containing `{ success: false }` being displayed as success because JavaScript checked only transport success.
- Buttons rendered on one tab but bound only after a different tab was opened.
- UI authorization flags that did not include every policy enforced by the POST endpoint.
- Role claims checked by type without validating their game-scoped value.
- Client-posted slot, GUID, or display-name values being trusted for RCON targeting and audit records instead of resolving a fresh canonical server/repository identity.
- Successful external side effects followed by failed persistence being reported as complete success or omitted from telemetry.
- Polling modules overlapping requests, losing cursors on empty responses, polling while hidden, retaining stale rows after source changes, or mutating state after disposal.
- Text escaping being reused in HTML attribute contexts where quotes also require encoding.
- Server-originated names or messages reaching Toastr without `escapeHtml` enabled.
- Whole-document settings updates overwriting independently owned runtime state.

Strict browser diagnostics are intentional. Unexpected external requests, same-origin request failures, HTTP errors, console errors, and page errors should be treated as defects until disproved. If cancellation is expected behavior, assert exactly one expected method/path and the browser abort reason rather than adding a broad allowlist. A one-off static asset failure should be reproduced with the focused pack; do not suppress it merely because a rerun passes.

## Known boundaries

- CoD4x lifecycle requests currently use whole-document `UpsertConfiguration`. In-process locking and pending-request rejection prevent duplicate requests within one portal process, but safe cross-process/agent concurrency requires an atomic operation-request endpoint or ETag/conditional write in `portal-repository`. `portal-web` currently consumes Repository packages `4.2.16`; complete the owner change, publish new packages, then update the consumer. Do not bridge this boundary with copied contracts or direct HTTP calls.
- Screenshot configuration is covered by existing parser, serializer, view-model, and controller tests. Runtime screenshot capture, gallery, and delete workflows were skipped because `portal-web` currently has policies but no product endpoints or views for those operations.
- Current suite counts and durations are published from TRX in each workflow/PR
  summary rather than maintained as a second, stale baseline here. The category
  contract guards the HTTP/browser partition, including generated Reqnroll
  features. Continue measuring runtime as new packs are added.
