# Azure App Configuration CLI - AI Development Guide

## Architecture Overview

**Two-layer design**: `AppConfigCli.Core` (domain logic) + `AppConfigCli` (CLI/editor)
- Core: Pure logic with `IConfigRepository` abstraction, state reconciliation, filtering helpers
- CLI: Azure integration, terminal UI, external editor workflows

**Key abstraction**: `IConfigRepository` with Azure (`AzureAppConfigRepository`) and in-memory (`InMemoryConfigRepository`) implementations for testing.

## Critical Patterns

### Item State Machine
Items flow through states: `Unchanged → Modified/New/Deleted → back to Unchanged`
- State reconciliation handles server updates while preserving local edits
- `AppStateReconciler.Reconcile()` merges server data with local state
- `ChangeApplier.ComputeChanges()` generates upserts/deletes for save operations

### Repository Pattern with Label Semantics
```csharp
// Core abstraction - implement this for different backends
public interface IConfigRepository {
    Task<IReadOnlyList<ConfigEntry>> ListAsync(string? prefix, string? labelFilter, CancellationToken ct = default);
}
```
Label handling: `null` = any label, `""` = empty label, other = literal match. Use `LabelFilter.ForSelector()` and `LabelFilter.ForWrite()` for proper Azure SDK mapping.

### Command Architecture (Partially Implemented)
- `CommandParser` + `Commands` pattern exists with tests but not yet wired to main loop
- Current: switch statement in `EditorApp.RunAsync()`
- Goal: Replace with parser-driven dispatch

### Mapping Strategy
Uses Mapperly for UI ↔ Core model conversion (`EditorMappers`). UI models add computed properties like `IsNew`, `IsDeleted` while Core models focus on pure state.

## Development Workflows

### Build & Test
```bash
dotnet build CodexAppConfig.sln           # Zero warnings expected
dotnet test -v minimal                    # Core: 39 tests, App: 28 tests
make coverage                            # Opens HTML coverage report
```

### Running with Different Auth
```bash
# Connection string (simplest)
export APP_CONFIG_CONNECTION_STRING="Endpoint=...;Id=...;Secret=..."
dotnet run --project src/AppConfigCli -- --prefix app:settings:

# Azure AD with endpoint discovery
dotnet run --project src/AppConfigCli -- --prefix app:settings: --auth device
```

### Editor Testing Patterns
Use `EditorApp.Test_*` properties and `InMemoryConfigRepository` for integration tests:
```csharp
var repo = new InMemoryConfigRepository([...]);
var app = new EditorApp(repo, "prefix:", "label");
await app.LoadAsync();
// Modify app.Test_Items directly, then app.Test_SaveAsync()
```

## Project-Specific Conventions

### File Organization
- `src/AppConfigCli.Core/`: Domain logic, no Azure dependencies
- `src/AppConfigCli/Editor/`: CLI command parsing, UI rendering, external editor integration
- Partial classes: `EditorApp` split into main class + `EditorApp.UI.cs` + `EditorApp.TestHooks.cs`

### Structured Editing
Two-phase edit workflow via `BulkEditHelper` and `StructuredEditHelper`:
1. Export visible items to text/JSON/YAML using `FlatKeyMapper`
2. Parse edited content back and reconcile via bulk operations

### Filtering Architecture
- `LabelFilter`: Maps UI labels to Azure SDK selectors
- `ItemFilter`: Combines label + regex filtering with visibility mapping
- Range operations (copy/delete/undo) use filtered indices, not absolute positions

### Testing Philosophy
- Core: Pure unit tests with no external dependencies
- App: Integration tests using `InMemoryConfigRepository`
- Async methods start with `await Task.CompletedTask` to avoid warnings

## Integration Points

### Azure App Configuration
- Connection string auth: Direct `ConfigurationClient` construction
- Azure AD: `DefaultAzureCredential` with endpoint discovery via ARM API
- Label semantics: `null` → any, `""` → unlabeled (`\0` in selectors)

### External Editor Integration
Abstractions `IFileSystem` and `IExternalEditor` enable testable external editor workflows. Default uses `VISUAL`/`EDITOR` env vars or platform-specific fallbacks.

## Next Development Tasks
1. Wire `CommandParser` into main loop (replace switch statement)
2. Add ETag support for concurrency handling
3. Implement search/grep functionality in table view
4. Package as self-contained binaries