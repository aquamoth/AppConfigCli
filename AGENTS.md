Project: Keyboard-first Azure App Configuration CLI (C# .NET 9)

Goal
- Provide a fast, cross-platform, keyboard-only CLI to browse, edit, copy, and manage key/values in Azure App Configuration under a given prefix ("section"), with label awareness and safe merging back to Azure.

Current State (2025-09)
- Tech: .NET 9 console app in `src/AppConfigCli`. Single entry: `Program.cs`.
- Auth:
  - Connection string via `APP_CONFIG_CONNECTION_STRING` (RW keys).
  - Azure AD fallback when no connection string: endpoint discovery + sign-in using chained credentials with `--auth` control.
    - Modes: auto (default), device, browser, cli, vscode.
    - WSL/headless prefers device code; Windows/macOS prefer browser.
    - Supports `--endpoint`, `--tenant` flags. If endpoint omitted, lists App Config stores via ARM and lets user pick one.
  - Normalizes shorthand endpoints (e.g., `sh-app-config` -> `https://sh-app-config.azconfig.io`).

Core Features
- List keys for `--prefix` with optional `--label` filter; shows per-row Label column.
- Keyboard-only command loop; no mouse needed.
- Inline value editor with initial text, arrow/home/end navigation, non-wrapping horizontal viewport and single-char ellipses.
- Dynamic table layout that adapts to console width; hides Value column on very narrow screens (<60 cols).
- Sorting: by ShortKey, then Label.
- Add: prompts Key first, then Label (uses active filter if set); prevents same-label duplicates.
- Edit: e|edit <n> edits value; state transitions: Unchanged/Modified.
- Delete: d|delete <n> [m] supports ranges; removes New rows immediately, marks existing as Deleted; prints summary for ranges.
- Undo: u|undo <n> [m]|all supports range or “all”; removes New, restores Deleted/Modified to Unchanged; prints summary.
- Copy: c|copy <n> [m] copies a range from current label to a target label; then switches filter to target; prints created/updated summary.
- Label filter: l|label [value] sets/clears at runtime (no arg clears). Changing label reloads.
- Reload: r|reload fetches from Azure and reconciles with pending local edits:
  - If server now matches local edit → mark Unchanged.
  - Locally Modified but deleted on server → become New (will add back on save).
  - Locally Deleted but already deleted on server → drop.
  - Locally New that appeared on server → Unchanged or Modified depending on value.
- Save: s|save upserts Modified/New and deletes Deleted; prints summary.
- Safe quit: q|quit|exit warns on unsaved changes; offers Save+Quit, Quit without saving, or Cancel.
- WhoAmI: w|whoami prints token claims (tid/oid/upn/appid) and endpoint in AAD mode.

CLI Options
- `--prefix <value>`: Required section/prefix.
- `--label <value>`: Optional label filter.
- `--endpoint <url|name>`: AAD mode endpoint or shorthand name.
- `--tenant <guid>`: AAD tenant to authenticate against.
- `--auth <auto|device|browser|cli|vscode>`: Select auth method; defaults to auto.

Commands (alphabetical)
- a|add: Add new key under current/selected label.
- c|copy <n> [m]: Copy rows n..m to a target label and switch to it.
- d|delete <n> [m]: Delete range; new items removed, existing marked.
- e|edit <n>: Edit a value.
- h|help|?: Command help (+ usage/options snapshot).
- l|label [value]: Change label filter; no arg clears.
- q|quit|exit: Quit, with save/cancel prompt when unsaved changes.
- r|reload: Reload from Azure and reconcile local changes.
- s|save: Save pending changes to Azure.
- u|undo <n> [m]|all: Undo range or all.
- w|whoami: Print identity/endpoint details (AAD).

Auth Notes
- AAD requires data-plane RBAC: App Configuration Data Reader/Owner on the store. Subscription Owner/Global Admin alone is insufficient.
- In WSL/headless, device code is preferred; presents a URL + code.
- Endpoint discovery uses ARM (subscriptions -> configurationStores) and requires ARM read permissions.

Build/Run Quick Start
- Build: `dotnet build CodexAppConfig.sln`
- Run (connection string):
  - `export APP_CONFIG_CONNECTION_STRING="Endpoint=...;Id=...;Secret=..."`
  - `dotnet run --project src/AppConfigCli -- --prefix app:settings:`
- Run (AAD + discovery):
  - `dotnet run --project src/AppConfigCli -- --prefix app:settings: --auth device`
  - Or force endpoint: `--endpoint https://<name>.azconfig.io` or `--endpoint <name>`
  - Optional: `--tenant <tenant-guid>`

Design Highlights
- Item state machine: Unchanged, Modified, New, Deleted.
- Save logic: upsert for New/Modified; delete for Deleted; removes local Deleted rows.
- Reload reconciliation preserves intent and avoids stale “modified” flags when server matches.
- Layout: width-aware columns; single-character ellipsis; hides Value on narrow screens.

Known Limitations / Future Enhancements
- No ETag/concurrency conflict handling; consider using `If-Match` with last ETag to avoid overwrites and prompt on conflicts.
- No bulk import/export (JSON/YAML) or search/filter in-table.
- Minimal validation for values (all strings).
- No test suite; consider unit tests for reconciliation, truncation/layout, and command parsing.
- Packaging: publish single-file binaries (win-x64/linux-x64), versioning and release scripts.

Next Session: Likely Tasks
- Add ETag-based conflict detection and resolve flow on save.
- Add search/filter and quick jump commands.
- Optional “preview save” summary with planned upserts/deletes before confirming.
- Packaging: `dotnet publish` self-contained targets; CI for releases.
- Telemetry/verbose logs toggle.

Repo Pointers
- Solution: `CodexAppConfig.sln`
- App: `src/AppConfigCli` (all logic in `Program.cs` for now)
- README: end-user build/run instructions and environment setup

How to Resume Quickly
1) `dotnet build CodexAppConfig.sln`
2) Run with your preferred auth:
   - ConnStr: set `APP_CONFIG_CONNECTION_STRING` → `dotnet run --project src/AppConfigCli -- --prefix <prefix>`
   - AAD: `dotnet run --project src/AppConfigCli -- --prefix <prefix> --auth device` and select a store
3) Use commands shown in footer (`h` for help). Focus areas: reload reconciliation, table layout, and label workflows.

Development Status (2025-09-16)
- Core extraction started: new library `src/AppConfigCli.Core` with domain models (`Item`, `ConfigEntry`, `ItemState`) and `AppStateReconciler`.
- Unit tests: new project `tests/AppConfigCli.Core.Tests` with initial reconciliation tests (3) passing.
- CLI integrates Core: `EditorApp.LoadAsync` now uses `AppStateReconciler`; mappings between CLI models and Core models are generated via `Riok.Mapperly`.
- Editor roundtrips: text (`open`), JSON (`json <sep>`), YAML (`yaml <sep>`) operate on visible items; arrays supported via numeric subkeys; improved error output for JSON/YAML.
- Filters: label filter supports `(any)`/`(none)` semantics (empty label via `l -`), and key regex filter (`g|grep`).
- Bug fixes: external-editor rename flow now resurrects deleted items and consolidates duplicates; refresh no longer crashes.
- Coverage: `make coverage` runs tests with Coverlet and produces HTML report under `coveragereport/index.html`.

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
