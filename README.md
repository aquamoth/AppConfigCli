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

- If you don’t set an endpoint, the tool will list your available App Configuration stores via Azure Resource Manager and let you pick one.
- Alternatively, set `APP_CONFIG_ENDPOINT` directly.
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
# --prefix is optional; you can set it later in-app with p|prefix
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

### Version
- Print version: `dotnet run --project src/AppConfigCli -- --version`
- Versioning uses Nerdbank.GitVersioning; output includes SemVer, optional branch, and commit:
  - On non-main branches: `v<semver>-<branch>+<commit>` (e.g., `v0.1.1-development+635e808`)
  - On main: `v<semver>+<commit>` (e.g., `v0.1.1+635e808`)

## Usage

- `--prefix <value>`: Optional. Initial key prefix (section) to load; you can change it in-app.
- `--label <value>`: Optional. Azure App Config label filter.
- `--endpoint <url>`: Optional. Azure App Configuration endpoint (used for AAD auth).
- `--tenant <guid>`: Optional. Entra ID tenant ID to sign into (AAD auth).
- `--auth <mode>`: Optional. Auth method: `auto` (default), `device`, `browser`, `cli`, or `vscode`.

Editor commands (no mouse required):

- `e <n>`: Edit value for item number `n`
- `a`: Add a new key (under the prefix)
- `o`: Open all visible items in your external editor (VISUAL/EDITOR, or notepad on Windows)
- `p [value]`: Change prefix (no arg prompts). Warns if there are unsaved changes, offering Save/Discard/Cancel.
- `d <n>`: Delete item `n` (asks for confirmation)
- `u <n> [m] | all`: Undo local changes for a range or all
- `s`: Save all changes to Azure
- `r`: Reload from Azure and reconcile local changes
- `l [value]`: Change label filter.
   - No arg: clear filter (any label)
   - `-`: explicitly empty label
   - Any other value: literal label
- `q`: Quit (warns on unsaved changes)
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
