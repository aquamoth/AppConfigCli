# Codex AppConfig CLI

Keyboard-first, cross-platform editor for Azure App Configuration sections, built in C# (.NET 9). It lists key/values under a prefix, lets you edit/add/delete using only the keyboard, and saves changes back by merging updates.

## Requirements

- .NET 9 SDK
- Azure App Configuration connection string, or an endpoint + Azure login

Option A: Connection string auth (simplest)

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

Option B: Azure AD auth (no connection string)

- Get your App Configuration endpoint, e.g. `https://<name>.azconfig.io`
- Set `APP_CONFIG_ENDPOINT` (or enter it when prompted at startup)
- Sign in via browser or device code when prompted
  - Your identity must be granted data-plane access on the App Configuration resource.
    Assign the built-in role "App Configuration Data Reader" (read) or "App Configuration Data Owner" (read/write).
  - WSL/headless Linux: the tool prefers Device Code auth if a browser cannot be opened. You’ll see a URL and a code to enter on any device.
    To enable browser launch in WSL, install `wslu` (for `wslview`) or ensure `xdg-open` works.

Linux/macOS (bash/zsh):
```bash
export APP_CONFIG_ENDPOINT="https://<name>.azconfig.io"
```

Windows (PowerShell):
```powershell
$env:APP_CONFIG_ENDPOINT = "https://<name>.azconfig.io"
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
- `--endpoint <url>`: Optional. Azure App Configuration endpoint (used for AAD auth).
- `--tenant <guid>`: Optional. Entra ID tenant ID to sign into (AAD auth).

Editor commands (no mouse required):

- `e <n>`: Edit value for item number `n`
- `a`: Add a new key (under the prefix)
- `d <n>`: Delete item `n` (asks for confirmation)
- `r <n>`: Revert local change for item `n`
- `s`: Save all changes to Azure
- `q`: Quit without saving
- `h`: Help
- `w`: WhoAmI (prints current identity and endpoint)

Legend: `*` modified, `+` new, `-` delete pending, ` ` unchanged

## Project Layout

- `CodexAppConfig.sln`: Solution file
- `src/AppConfigCli`: .NET 9 console app

## Implementation Notes

- Uses `Azure.Data.AppConfiguration` with a connection string for simple, cross-platform auth.
- If `APP_CONFIG_CONNECTION_STRING` is not set, falls back to Azure AD auth against `APP_CONFIG_ENDPOINT` using chained credentials (Interactive Browser → Device Code → Azure CLI → VS Code).
- Azure RBAC is required for AAD auth: grant your user/service principal "App Configuration Data Reader" or "App Configuration Data Owner" on the App Configuration resource.
- Save performs upsert for new/changed keys and delete for deletions; other keys under the same prefix are untouched.
