Project: Keyboard-first Azure App Configuration CLI (C# .NET 9)

Goal
- Provide a fast, cross-platform, keyboard-only CLI to browse, edit, copy, and manage key/values in Azure App Configuration under a given prefix ("section"), with label awareness and safe merging back to Azure.

Current State (2025-09-21)
- Tech: .NET 9 console app in `src/AppConfigCli` with core domain in `src/AppConfigCli.Core`.
- Auth:
  - Connection string via `APP_CONFIG_CONNECTION_STRING` (RW keys).
  - Azure AD fallback when no connection string: endpoint discovery + sign-in using chained credentials with `--auth` control.
    - Modes: auto (default), device, browser, cli, vscode.
    - WSL/headless prefers device code; Windows/macOS prefer browser.
    - Supports `--endpoint`, `--tenant` flags. If endpoint omitted, lists App Config stores via ARM and lets user pick one.
  - Normalizes shorthand endpoints (e.g., `sh-app-config` -> `https://sh-app-config.azconfig.io`).

Core Features
- List keys for `--prefix` with optional `--label` filter; hides the Label column when a label filter is active (more room for Key/Value).
- Keyboard-only command loop; no mouse needed.
- Inline value editor with initial text, arrow/home/end navigation, non-wrapping horizontal viewport and single-char ellipses.
- Dynamic table layout that adapts to console width; hides Value column on very narrow screens (<60 cols). Per-page column sizing: index width based on visible indices, key/label/value sized from the longest values visible on the page; spare width flows to Value.
- Sorting: by ShortKey, then Label.
- Add: `a|add [key]` prompts Key (if omitted) then Label (uses active filter if set); prevents same-label duplicates; ESC/Ctrl+C cancels at any prompt.
- Edit: Numeric edit — typing `<n>` edits value of item `n`; state transitions: Unchanged/Modified; ESC/Ctrl+C cancels.
- Delete: d|delete <n> [m] supports ranges; removes New rows immediately, marks existing as Deleted; prints summary for ranges.
- Undo: u|undo <n> [m]|all supports range or “all”; removes New, restores Deleted/Modified to Unchanged; prints summary.
- Copy: c|copy <n> [m] copies a range from current label to a target label; then switches filter to target; prints created/updated summary.
- Label filter: l|label [value] sets/clears at runtime (no arg clears). Changing label reloads.
- Reload: r|reload fetches from Azure and reconciles with pending local edits (Unchanged/Modified/New rules handled by Core `AppStateReconciler`). Auto-invalidate prefix cache.
- Save: s|save upserts Modified/New and deletes Deleted; prints summary (`ChangeApplier`).
- Safe quit: q|quit|exit warns on unsaved changes; offers Save+Quit, Quit without saving, or Cancel.
- WhoAmI: whoami prints token claims (tid/oid/upn/appid) and endpoint in AAD mode.
- Grep: Primary shortcut `/` (no space required, e.g. `/user:`). Full `grep` and hidden shortcut `g` require a space (e.g. `grep foo`). Filters keys case-insensitively.
- Header: Title shows `PAGE x/y` right-aligned; below it, Prefix/Label/Filter only show when active; values are colorized; layout adapts to width.
- Pagination: PageUp/PageDown moves pages; screen auto-refreshes on terminal resize.
- Prompt: Single-line `Command (h for help)>`.
- Prefix intellisense: When using `p|prefix` with no arg, inline autocomplete over all known prefixes (ends with `/`), using Tab to accept, Up/Down to cycle; ESC/Ctrl+C cancels. Suggestions search across the whole store (cached), not just filtered rows.
- Responsiveness: Per-page timeouts on Azure listing to avoid hangs; conservative retries; low-overhead request counting and summary on exit.

CLI Options
- `--prefix <value>`: Required section/prefix.
- `--label <value>`: Optional label filter.
- `--endpoint <url|name>`: AAD mode endpoint or shorthand name.
- `--tenant <guid>`: AAD tenant to authenticate against.
- `--auth <auto|device|browser|cli|vscode>`: Select auth method; defaults to auto.

Commands (alphabetical)
- a|add [key]: Add new key under current/selected label.
- c|copy <n> [m]: Copy rows n..m to a target label and switch to it.
- d|delete <n> [m]: Delete range; new items removed, existing marked.
- <n>: Edit a value for row n (numeric).
- h|help|?: Command help (+ usage/options snapshot).
- l|label [value]: Change label filter; no arg clears.
- q|quit|exit: Quit, with save/cancel prompt when unsaved changes.
- r|reload: Reload from Azure and reconcile local changes.
- s|save: Save pending changes to Azure.
- u|undo <n> [m]|all: Undo range or all.
- whoami: Print identity/endpoint details (AAD).
- /|grep [regex]: Filter keys by regex (case-insensitive). `/pattern` doesn’t need a space; `grep pattern` and `g pattern` do.
- j|json [sep]: Open visible items in JSON editor with key separator (default `:`).
- y|yaml [sep]: Open visible items in YAML editor with key separator (default `:`).

Auth Notes
- AAD requires data-plane RBAC: App Configuration Data Reader/Owner on the store. Subscription Owner/Global Admin alone is insufficient.
- In WSL/headless, device code is preferred; presents a URL + code.
- Endpoint discovery uses ARM (subscriptions -> configurationStores) and requires ARM read permissions.

Build/Run Quick Start
- Build: `dotnet build AppConfigCli.sln`
- Run (connection string):
  - `export APP_CONFIG_CONNECTION_STRING="Endpoint=...;Id=...;Secret=..."`
  - `dotnet run --project src/AppConfigCli -- --prefix app:settings:`
- Run (AAD + discovery):
  - `dotnet run --project src/AppConfigCli -- --prefix app:settings: --auth device`
  - Or force endpoint: `--endpoint https://<name>.azconfig.io` or `--endpoint <name>`
  - Optional: `--tenant <tenant-guid>`
- Tests: `dotnet test AppConfigCli.sln --nologo --verbosity minimal` (Core + App test projects; zero warnings on build)

Design Highlights
- Item state machine: Unchanged, Modified, New, Deleted (Core + UI models mapped via Mapperly).
- Repository abstraction: `IConfigRepository`, with `AzureAppConfigRepository` and `InMemoryConfigRepository`.
- Reconciliation: `AppStateReconciler` preserves intent; `ChangeApplier` computes upserts/deletes.
- Layout helpers (Core): `TextTruncation`, `TableLayout` with unit tests.
- Filtering helpers (Core): `LabelFilter`, `ItemFilter` with tests (visible indices, range mapping, label semantics).
- Structured editors:
  - Bulk text editor (`open`): `BulkEditHelper` handles reconcile from tab-separated edits.
  - JSON/YAML editors (`json <sep>`, `yaml <sep>`): `StructuredEditHelper` builds/reads via `FlatKeyMapper`, normalizes YAML nodes, and applies using bulk reconcile rules.
- Editor app modularization:
  - Partial class split (`EditorApp.UI.cs`), `IFileSystem` and `IExternalEditor` abstractions for testability.
  - `CommandParser` drives the main loop; numeric edit, paging keys, and special-case `/` are handled.
- Zero build warnings (async methods start with `await Task.CompletedTask` where needed).

Service Interaction & Resilience
- Azure client uses conservative retry/backoff (MaxRetries=2, exponential).
- Listing uses paged enumeration with per-page timeouts to avoid hangs under throttling; returns partial results rather than blocking.
- A lightweight pipeline policy counts HTTP tries, successes/failures, and status distribution (429, etc.); a summary prints on quit.

Known Limitations / Future Enhancements
- ETag/concurrency not implemented yet (no `If-Match`); plan to add ETag to models and conflict workflow.
- `CommandParser` not wired into the command loop (exists with tests); wire it and simplify `RunAsync`.
- Search/quick-jump in table not implemented.
- Packaging: publish single-file binaries (win-x64/linux-x64), versioning and CI releases.

Next Session: Likely Tasks
- Concurrency: Add optional ETags to Core models; extend repos to support `If-Match`; implement conflict handling UX and tests.
- Wire `CommandParser` into `RunAsync` to replace switch; add tests for dispatch.
- Add search/quick-jump commands and tests.
- Optional: “preview save” summary before committing changes.
- Packaging/CI: publish self-contained binaries; add GitHub Actions (build, test, coverage artifacts).

Repo Pointers
- Solution: `AppConfigCli.sln`
- App: `src/AppConfigCli` (CLI + editor; modularized via partials/abstractions)
- Core: `src/AppConfigCli.Core` (domain, filters, reconciliation, layout)
- Tests:
  - `tests/AppConfigCli.Core.Tests` (Core unit tests)
  - `tests/AppConfigCli.Tests` (App-level tests: parser, range mapper, bulk/structured edit, integration)
- README: end-user build/run instructions and environment setup

How to Resume Quickly
1) `dotnet build AppConfigCli.sln`
2) `dotnet test AppConfigCli.sln --nologo --verbosity minimal` (should be all green, zero build warnings)
3) Run with your preferred auth:
   - ConnStr: set `APP_CONFIG_CONNECTION_STRING` → `dotnet run --project src/AppConfigCli -- --prefix <prefix>`
   - AAD: `dotnet run --project src/AppConfigCli -- --prefix <prefix> --auth device` and select a store
4) Try editors:
   - Bulk text: `o|open`
   - JSON: `j|json :` (or your separator)
   - YAML: `y|yaml :` (or your separator)
5) For development, focus on: ETag flow, packaging, and optional live stats/UX for throttling.

Development Status (2025-09-19)
- Modularization:
  - Split rendering to `EditorApp.UI.cs`; added `IFileSystem`/`IExternalEditor` for testability.
  - Extracted Bulk/Structured edit helpers; JSON/YAML apply now uses helpers exclusively.
- Core helpers and filters added with tests: `LabelFilter`, `ItemFilter`, `FlatKeyMapper`, `ChangeApplier`, `TextTruncation`, `TableLayout`.
- Command parsing and range mapping: `CommandParser`, `Commands`, and `RangeMapper` implemented with tests (parser not yet wired).
- Integration tests: non-interactive `EditorApp` flows with `InMemoryConfigRepository`.
- Build is warning-free; tests count: Core 39, App 28.

Refactor & Testing Roadmap (next steps)
- LabelFilter service (+ tests):
  - Map for selector: `null` → `null` (any), `""` → `"\0"` (unlabeled), other → literal.
  - Map for writes/deletes: `null`/`""` → `null`, other → literal.
- ItemFilter service (+ tests):
  - Compose label + regex predicates; return visible list; provide range/index mapping helpers used by copy/delete/undo.
- FlatKeyMapper (+ tests):
  - Split/join keys with separator; support arrays (numeric segments) and `__value` for value+children.
  - Roundtrip tests mirroring JSON/YAML editor behavior.
- ChangeApplier (+ tests):
  - From local states → upsert/delete sets; handle resurrect-on-readd and duplicate consolidation rules.
- Repository abstraction:
  - Define `IConfigRepository` and move Azure SDK calls into `AzureAppConfigRepository`.
  - Add `InMemoryConfigRepository` for fast unit tests; adapt `EditorApp` to depend on the interface.
- Rendering/view helpers (+ tests):
  - Extract truncation (`TruncateFixed`), layout (`ComputeLayout`), and formatting; add tests for width thresholds and ellipsis behavior.
- Mapperly mapping configuration (+ tests as needed):
  - Keep UI-only helpers ignored; validate enum/state mapping stays consistent if names change.
- Concurrency (later):
  - Extend models with ETags; add `If-Match` logic and a conflict resolution workflow; unit-test the decision logic.
- CI (later):
  - GitHub Actions to build, test, and publish coverage reports (Cobertura + HTML artifact).

How to Run Coverage Locally
- `make coverage` → opens report under `coveragereport/index.html` (generated by ReportGenerator).
- Raw Cobertura XML: `tests/**/TestResults/*/coverage.cobertura.xml`.
