# Codex AppConfig CLI

Keyboard-first, cross-platform editor for Azure App Configuration sections, built in C# (.NET 9). It lists key/values under a prefix, lets you edit/add/delete using only the keyboard, and saves changes back by merging updates.

## Requirements

- .NET 9 SDK
- Azure App Configuration connection string

Set the connection string in your environment:

Linux/macOS (bash/zsh):
```bash
export APP_CONFIG_CONNECTION_STRING="Endpoint=...;Id=...;Secret=..."
export APP_CONFIG_LABEL="dev"   # optional
```

Windows (PowerShell):
```powershell
$env:APP_CONFIG_CONNECTION_STRING = "Endpoint=...;Id=...;Secret=..."
$env:APP_CONFIG_LABEL = "dev"   # optional
```

## Build and Run

Using the solution:
```bash
dotnet restore CodexAppConfig.sln
dotnet build CodexAppConfig.sln
dotnet run --project src/AppConfigCli -- --prefix app:settings: --label "$APP_CONFIG_LABEL"
```

Directly from the project:
```bash
dotnet run --project src/AppConfigCli -- --prefix app:settings: --label dev
```

With Makefile:
```bash
make build
make run prefix=app:settings: label=dev
```

## Usage

- `--prefix <value>`: Required. Key prefix (section) to edit.
- `--label <value>`: Optional. Azure App Config label filter.

Editor commands (no mouse required):

- `e <n>`: Edit value for item number `n`
- `a`: Add a new key (under the prefix)
- `d <n>`: Delete item `n` (asks for confirmation)
- `r <n>`: Revert local change for item `n`
- `s`: Save all changes to Azure
- `q`: Quit without saving
- `h`: Help

Legend: `*` modified, `+` new, `-` delete pending, ` ` unchanged

## Project Layout

- `CodexAppConfig.sln`: Solution file
- `src/AppConfigCli`: .NET 9 console app

## Implementation Notes

- Uses `Azure.Data.AppConfiguration` with a connection string for simple, cross-platform auth.
- Save performs upsert for new/changed keys and delete for deletions; other keys under the same prefix are untouched.
