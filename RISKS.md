# Stability Risks and Refactor Targets

- Monolithic EditorApp/Program.cs coupling (Risk: 9/10): Core loop, IO, rendering, paging, and state live together (Program.cs + partials). This makes changes risky, fosters duplication, and complicates testability. Extract UI/input/rendering into focused components to reduce blast radius.

- Fragmented input editors (Risk: 9/10): Multiple prompt editors with overlapping logic — ReadLineOrCtrlC, Edit.ReadLineWithInitial, Add.ReadLineCancelable, ReadLineWithPagingCancelable — implement similar navigation/deletion differently, risking inconsistency and regressions. Consolidate into a single reusable line-edit control.

- Duplicate header layout logic (Risk: 8/10): BuildFilterHeaderLines and RenderFilterHeader both compute/position headers with diverging heuristics. Changes to one can desync UI. Unify header computation + rendering with a single source of truth.

- Stray/duplicate structured tree builders vs FlatKeyMapper (Risk: 7/10): Yaml.cs and Program.cs contain AddPathDict/AddPathList helpers while JSON/YAML flows already rely on Core.FlatKeyMapper. These extra implementations can drift and confuse maintenance. Remove or centralize behind FlatKeyMapper.

- Console rendering with ad‑hoc try/catch (Risk: 6/10): Many direct Console calls wrapped in empty catch blocks (positioning, size reads, clears). Silent failures may leave inconsistent UI and mask platform quirks. Introduce a thin console abstraction with safe ops + consistent error handling.

- Mutable shared state on EditorApp (Risk: 6/10): Commands mutate EditorApp.Items/Label/Prefix/ValueHighlightRegex directly. Invariants (sorting, duplicate consolidation, state transitions) depend on each caller remembering to tidy up. Encapsulate state changes via methods that enforce invariants atomically.

- Network/cancellation ergonomics (Risk: 5/10): UI calls (LoadAsync/SaveAsync) lack cancellation tokens. Under throttling/latency, the app may appear stuck. Wire CancellationToken and surface progress/partial updates without changing behavior.

- Rendering/colorization duplication (Risk: 5/10): Several colored-output helpers (WriteColored, WriteColoredFixed, WriteColoredFixedWithHighlight, Edit.WriteColoredInline) duplicate classification and reset logic. Centralize color classification and fixed-width writing for consistency.

- Test coverage gaps for UI primitives (Risk: 4/10): Layout/paging/header logic is complex but largely untested at the App layer (Core has TextTruncation tests). Extract pure helpers (width calc, paging math, header layout) and cover with unit tests.

- External editor/file IO robustness (Risk: 3/10): Temp path/editor launch handled with try/finally, but a thin file/editor wrapper with clearer errors and basic guardrails would harden flows (e.g., non-writable temp dirs) without changing behavior.

- Logging/diagnostics (Risk: 2/10): Reliance on Console alone makes diagnosing intermittent issues harder. Add a small diagnostic sink (in-memory ring buffer, optional verbose toggle) to aid support without affecting flow.


# What to fix first (ordered, minimal effort → high payoff)

1. Protect the entry path (Program)

  * Extract a small Bootstrap function that accepts args + an environment abstraction; test it with a few smoke cases (no args, invalid command, happy path). Even 6–10 tests will jump this from 0% to meaningful. 
GitHub

2. Cover AzureAppConfigRepository without real Azure

  * Invert the HTTP/Azure SDK boundary behind an interface; write tests using a fake/mocked client to exercise: paging, timeouts/backoff, throttling, and error mapping. Your report mentions conservative retry/backoff; none of that is covered right now. Target ~60%+ here. 

3. Stabilize the command surface (Command, CommandParser)

  * Add table-driven tests for each command, including error paths and help output.

  * Add property-based tests for parsing (e.g., random whitespace, quoting, edge tokens). Aim >80% lines / 60% branches. 

4. Chip away at EditorApp flow (don’t aim for perfection)

  * Introduce a console/terminal abstraction; write scenario tests for: paging, / filter, cancel/ESC, and replace/grep flows that the PR adds. Cover the “happy paths + one failure per feature.” Moving from 6.5% → 35–40% will drastically reduce surprise regressions. 

