using Azure;
using Azure.Data.AppConfiguration;
using System.Text;
using Azure.Identity;
using Azure.Core;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using AppConfigCli.Core;
using AppConfigCli.Core.UI;
using CoreItem = AppConfigCli.Core.Item;
using CoreItemState = AppConfigCli.Core.ItemState;
using CoreConfigEntry = AppConfigCli.Core.ConfigEntry;

namespace AppConfigCli;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var options = ParseArgs(args);
        if (options.ShowVersion)
        {
            Console.WriteLine(VersionInfo.GetVersionLine());
            return 0;
        }
        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        // --prefix is now optional; user can set/change it in-app

        var connStr = Environment.GetEnvironmentVariable("APP_CONFIG_CONNECTION_STRING");
        ConfigurationClient client;
        Func<Task>? whoAmIAction = null;
        string authModeDesc;
        if (!string.IsNullOrWhiteSpace(connStr))
        {
            client = new ConfigurationClient(connStr);
            authModeDesc = "connection-string";
            whoAmIAction = () =>
            {
                Console.WriteLine("Auth: connection string");
                return Task.CompletedTask;
            };
        }
        else
        {
            // Fallback to AAD auth via endpoint + interactive/device/browser auth
            var tenantId = options.TenantId ?? Environment.GetEnvironmentVariable("AZURE_TENANT_ID");
            var credential = BuildCredential(tenantId, options.Auth);

            var endpoint = options.Endpoint ?? Environment.GetEnvironmentVariable("APP_CONFIG_ENDPOINT");
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                Console.WriteLine("No endpoint provided. Discovering App Configuration stores via Azure…");
                endpoint = await TrySelectEndpointAsync(credential);
                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    Console.WriteLine("Could not list stores or none found. Enter endpoint manually (e.g., https://<name>.azconfig.io):");
                    Console.Write("> ");
                    endpoint = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(endpoint))
                    {
                        Console.Error.WriteLine("No endpoint provided. Exiting.");
                        return 2;
                    }
                }
            }

            var uri = NormalizeEndpoint(endpoint!);
            client = new ConfigurationClient(uri, credential);
            var authLabel = string.IsNullOrWhiteSpace(options.Auth) ? "auto" : options.Auth;
            authModeDesc = $"aad ({(tenantId ?? "default tenant")}, auth={authLabel})";
            whoAmIAction = async () => await WhoAmIAsync(credential, uri);
        }

        var repo = new AzureAppConfigRepository(client);
        // If no theme is specified, force the built-in 'default' preset
        var theme = ConsoleTheme.Load(options.Theme ?? "default", options.NoColor);
        var app = new EditorApp(repo, options.Prefix, options.Label, whoAmIAction, authModeDesc, theme: theme);
        try
        {
            // (obsolete nested tree helpers removed; structured editors use Core.FlatKeyMapper)
            await app.LoadAsync();
            await app.RunAsync();
            return 0;
        }
        catch (RequestFailedException rfe) when (rfe.Status == 403)
        {
            Console.Error.WriteLine("Forbidden (403): The signed-in identity lacks App Configuration data access.");
            Console.Error.WriteLine("Grant App Configuration Data Reader (read) or Data Owner (read/write) on the target App Configuration resource.");
            Console.Error.WriteLine("Alternatively, use a connection string in APP_CONFIG_CONNECTION_STRING.");
            Console.Error.WriteLine("Tip: If you were silently authenticated via Azure CLI, try signing in interactively with a different account by logging out of Azure CLI or using the device code prompt.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static TokenCredential BuildCredential(string? tenantId, string? authMode)
    {
        var browser = new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
        {
            TenantId = tenantId,
            DisableAutomaticAuthentication = false
        });
        var cli = new AzureCliCredential();
        var vsc = new VisualStudioCodeCredential(new VisualStudioCodeCredentialOptions
        {
            TenantId = tenantId
        });
        var device = new DeviceCodeCredential(new DeviceCodeCredentialOptions
        {
            TenantId = tenantId,
            ClientId = "04b07795-8ddb-461a-bbee-02f9e1bf7b46",
            DeviceCodeCallback = (code, ct) =>
            {
                Console.WriteLine();
                Console.WriteLine("To sign in, open the browser to:");
                Console.WriteLine(code.VerificationUri);
                Console.WriteLine("and enter the code:");
                Console.WriteLine(code.UserCode);
                Console.WriteLine();
                return Task.CompletedTask;
            }
        });

        switch (authMode?.ToLowerInvariant())
        {
            case "device": return device;
            case "browser": return browser;
            case "cli": return cli;
            case "vscode": return vsc;
            case null:
            case "auto":
            default:
                bool isWsl = IsWsl();
                bool hasDisplay = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));
                if (isWsl || (!OperatingSystem.IsWindows() && !hasDisplay))
                {
                    return new ChainedTokenCredential(device, browser, cli, vsc);
                }
                else
                {
                    return new ChainedTokenCredential(browser, device, cli, vsc);
                }
        }
    }

    private static bool IsWsl()
    {
        try
        {
            if (!OperatingSystem.IsLinux()) return false;
            var wslEnv = Environment.GetEnvironmentVariable("WSL_DISTRO_NAME");
            if (!string.IsNullOrEmpty(wslEnv)) return true;
            string path1 = "/proc/sys/kernel/osrelease";
            if (File.Exists(path1))
            {
                var txt = File.ReadAllText(path1);
                if (txt.Contains("microsoft", StringComparison.OrdinalIgnoreCase)) return true;
            }
            string path2 = "/proc/version";
            if (File.Exists(path2))
            {
                var txt = File.ReadAllText(path2);
                if (txt.Contains("microsoft", StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        catch { }
        return false;
    }

    private static async Task WhoAmIAsync(TokenCredential credential, Uri endpoint)
    {
        try
        {
            var ctx = new TokenRequestContext(new[] { "https://azconfig.io/.default" });
            var token = await credential.GetTokenAsync(ctx, default);
            var payload = DecodeJwtPayload(token.Token);
            string GetStr(string name) => payload.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            Console.WriteLine("Auth: Azure AD token");
            Console.WriteLine($"Endpoint: {endpoint}");
            Console.WriteLine($"TenantId (tid): {GetStr("tid")}");
            Console.WriteLine($"ObjectId (oid): {GetStr("oid")}");
            Console.WriteLine($"User/UPN: {GetStr("preferred_username")}");
            Console.WriteLine($"AppId (appid): {GetStr("appid")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"whoami failed: {ex.Message}");
        }
    }

    private static async Task<string?> TrySelectEndpointAsync(TokenCredential credential)
    {
        try
        {
            // Get ARM token
            var token = await credential.GetTokenAsync(new TokenRequestContext(new[] { "https://management.azure.com/.default" }), default);
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);

            // List subscriptions
            var subsResp = await http.GetAsync("https://management.azure.com/subscriptions?api-version=2020-01-01");
            if (!subsResp.IsSuccessStatusCode)
            {
                return null;
            }
            using var subsDoc = JsonDocument.Parse(await subsResp.Content.ReadAsStringAsync());
            var subs = new List<SubRef>();
            if (subsDoc.RootElement.TryGetProperty("value", out var subsArr) && subsArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in subsArr.EnumerateArray())
                {
                    subs.Add(new SubRef
                    {
                        Id = e.GetProperty("subscriptionId").GetString() ?? string.Empty,
                        Name = e.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? string.Empty : string.Empty
                    });
                }
            }

            var stores = new List<(string Name, string ResourceGroup, string Location, string Endpoint)>();
            foreach (var sub in subs)
            {
                string subId = sub.Id;
                var url = $"https://management.azure.com/subscriptions/{subId}/providers/Microsoft.AppConfiguration/configurationStores?api-version=2023-03-01";
                var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode) continue;
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                if (!doc.RootElement.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
                foreach (var item in arr.EnumerateArray())
                {
                    var name = item.GetProperty("name").GetString() ?? "";
                    var id = item.GetProperty("id").GetString() ?? "";
                    var location = item.TryGetProperty("location", out var locEl) ? (locEl.GetString() ?? "") : "";
                    string rg = "";
                    var parts = id.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i + 1 < parts.Length; i++)
                    {
                        if (parts[i].Equals("resourceGroups", StringComparison.OrdinalIgnoreCase))
                        {
                            rg = parts[i + 1];
                            break;
                        }
                    }
                    var endpoint = item.GetProperty("properties").GetProperty("endpoint").GetString() ?? "";
                    if (!string.IsNullOrEmpty(endpoint))
                        stores.Add((name, rg, location, endpoint));
                }
            }

            if (stores.Count == 0) return null;

            Console.WriteLine();
            Console.WriteLine("Select App Configuration store:");
            for (int i = 0; i < stores.Count; i++)
            {
                var s = stores[i];
                Console.WriteLine($"  {i + 1}. {s.Name}  rg:{s.ResourceGroup}  loc:{s.Location}  {s.Endpoint}");
            }
            Console.Write("Choice [1-" + stores.Count + "]: ");
            var sel = Console.ReadLine();
            if (int.TryParse(sel, out var idx) && idx >= 1 && idx <= stores.Count)
            {
                return stores[idx - 1].Endpoint;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static Uri NormalizeEndpoint(string endpoint)
    {
        var e = (endpoint ?? string.Empty).Trim();
        if (Uri.TryCreate(e, UriKind.Absolute, out var abs) &&
            (abs.Scheme == Uri.UriSchemeHttps || abs.Scheme == Uri.UriSchemeHttp))
        {
            return abs;
        }
        if (e.Contains("azconfig.io", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri("https://" + e.TrimStart('/'));
        }
        return new Uri($"https://{e}.azconfig.io");
    }

    private sealed class SubRef
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    private static JsonElement DecodeJwtPayload(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2) throw new InvalidOperationException("Invalid JWT");
        var payload = parts[1];
        string s = payload.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        var bytes = Convert.FromBase64String(s);
        using var doc = JsonDocument.Parse(bytes);
        // Return a clone so document disposal doesn’t invalidate
        return JsonDocument.Parse(doc.RootElement.GetRawText()).RootElement;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Azure App Configuration Section Editor");
        Console.WriteLine();
        Console.WriteLine("Usage: appconfig [--prefix <keyPrefix>] [--label <label>] [--endpoint <url>] [--tenant <guid>] [--auth <mode>] [--theme <name>] [--no-color] [--version]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --prefix <value>    Optional. Key prefix (section) to load initially");
        Console.WriteLine("  --label <value>     Optional. Label filter");
        Console.WriteLine("  --endpoint <url>    Optional. App Configuration endpoint for AAD auth");
        Console.WriteLine("  --tenant <guid>     Optional. Entra ID tenant for AAD auth");
        Console.WriteLine("  --auth <mode>       Optional. Auth method: auto|device|browser|cli|vscode (default: auto)");
        Console.WriteLine("  --theme <name>      Optional. Theme preset: default|mono|no-color|solarized");
        Console.WriteLine("  --no-color          Optional. Disable color output (overrides theme)");
        Console.WriteLine("  --version           Print version and exit");
        Console.WriteLine();
        Console.WriteLine("Environment:");
        Console.WriteLine("  APP_CONFIG_CONNECTION_STRING  Azure App Configuration connection string");
        Console.WriteLine("  APP_CONFIG_ENDPOINT           App Configuration endpoint (AAD auth)");
        Console.WriteLine("  AZURE_TENANT_ID               Entra ID tenant to authenticate against (AAD)");
        Console.WriteLine();
        Console.WriteLine("Commands inside editor:");
        Console.WriteLine("  a|add           Add new key under prefix");
        Console.WriteLine("  p|prefix [val]  Change prefix (no arg prompts)");
        Console.WriteLine("  c|copy <n> [m]  Copy rows n..m to another label and switch");
        Console.WriteLine("  d|delete <n> [m]  Delete items n..m");
        Console.WriteLine("  e|edit <n>      Edit item n");
        Console.WriteLine("  h|help          Help");
        Console.WriteLine("  l|label [value] Change label filter (no arg clears; '-' = empty label)");
        Console.WriteLine("  o|open          Edit all visible items in external editor");
        Console.WriteLine("  replace         Regex search+replace across visible VALUES");
        Console.WriteLine("  q|quit          Quit");
        Console.WriteLine("  r|reload        Reload from Azure and reconcile local changes");
        Console.WriteLine("  s|save          Save all changes");
        Console.WriteLine("  u|undo <n> [m]|all  Undo selection or all pending changes");
        Console.WriteLine();
    }

    private static Options ParseArgs(string[] args)
    {
        var opts = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--prefix":
                    if (i + 1 < args.Length) { opts.Prefix = args[++i]; }
                    break;
                case "--label":
                    if (i + 1 < args.Length) { opts.Label = args[++i]; }
                    break;
                case "--endpoint":
                    if (i + 1 < args.Length) { opts.Endpoint = args[++i]; }
                    break;
                case "--tenant":
                    if (i + 1 < args.Length) { opts.TenantId = args[++i]; }
                    break;
                case "--auth":
                    if (i + 1 < args.Length) { opts.Auth = args[++i].ToLowerInvariant(); }
                    break;
                case "--theme":
                    if (i + 1 < args.Length) { opts.Theme = args[++i]; }
                    break;
                case "--no-color":
                    opts.NoColor = true;
                    break;
                case "--version":
                    opts.ShowVersion = true;
                    break;
                case "-h":
                case "--help":
                    opts.ShowHelp = true;
                    break;
            }
        }
        return opts;
    }

    private sealed class Options
    {
        public string? Prefix { get; set; }
        public string? Label { get; set; }
        public bool ShowHelp { get; set; }
        public string? Endpoint { get; set; }
        public string? TenantId { get; set; }
        public string? Auth { get; set; } // device|browser|cli|vscode|auto
        public bool ShowVersion { get; set; }
        public string? Theme { get; set; }
        public bool NoColor { get; set; }
    }
}

// UI models moved to Editor/UiModels.cs

internal sealed partial class EditorApp
{
    private readonly AppConfigCli.Core.IConfigRepository _repo;
    private readonly string _authModeDesc;
    internal readonly Func<Task>? WhoAmI;


    internal string? Prefix { get; set; }
    internal string? Label { get; set; }
    internal List<Item> Items { get; } = [];
    internal string? KeyRegexPattern { get; set; }
    internal Regex? KeyRegex { get; set; }
    internal Regex? ValueHighlightRegex { get; set; }
    internal IFileSystem Filesystem { get; init; }
    internal IExternalEditor ExternalEditor { get; init; }
    internal ConsoleTheme Theme { get; init; }
    internal List<string> CommandHistory { get; } = new List<string>();

    public EditorApp(AppConfigCli.Core.IConfigRepository repo, string? prefix, string? label, Func<Task>? whoAmI = null, string authModeDesc = "", IFileSystem? fs = null, IExternalEditor? externalEditor = null, ConsoleTheme? theme = null)
    {
        _repo = repo;
        Prefix = prefix;
        Label = label;
        WhoAmI = whoAmI;
        _authModeDesc = authModeDesc;
        Filesystem = fs ?? new DefaultFileSystem();
        ExternalEditor = externalEditor ?? new DefaultExternalEditor();
        Theme = theme ?? ConsoleTheme.Load();
    }

    // Paging state
    private int _pageIndex = 0; // zero-based

    private void PageUp()
    {
        var total = GetVisibleItems().Count;
        int pageSize, pageCount;
        try { int h = Console.WindowHeight; int w = Console.WindowWidth; ComputePaging(h, total, GetHeaderLineCountForWidth(Math.Max(20, Math.Min(w, 240))), out pageSize, out pageCount); }
        catch { ComputePaging(40, total, GetHeaderLineCountForWidth(100), out pageSize, out pageCount); }
        if (pageCount <= 1) { _pageIndex = 0; return; }
        _pageIndex = Math.Max(0, _pageIndex - 1);
    }

    private void PageDown()
    {
        var total = GetVisibleItems().Count;
        int pageSize, pageCount;
        try { int h = Console.WindowHeight; int w = Console.WindowWidth; ComputePaging(h, total, GetHeaderLineCountForWidth(Math.Max(20, Math.Min(w, 240))), out pageSize, out pageCount); }
        catch { ComputePaging(40, total, GetHeaderLineCountForWidth(100), out pageSize, out pageCount); }
        if (pageCount <= 1) { _pageIndex = 0; return; }
        _pageIndex = Math.Min(pageCount - 1, _pageIndex + 1);
    }

    // Allow commands to trigger paging during custom prompts
    internal void PageUpCommand() => PageUp();
    internal void PageDownCommand() => PageDown();

    public async Task LoadAsync()
    {
        // Build server snapshot
        var server = (await _repo.ListAsync(Prefix, Label)).ToList();

        // Map local items to Core using Mapperly
        var mapper = new EditorMappers();
        var local = Items.Select(mapper.ToCoreItem).ToList();

        var reconciler = new AppStateReconciler();
        var freshCore = reconciler.Reconcile(Prefix ?? string.Empty, Label, local, server);

        Items.Clear();
        foreach (var it in freshCore)
        {
            Items.Add(mapper.ToUiItem(it));
        }
    }

    public async Task RunAsync()
    {
        var prevTreatCtrlC = Console.TreatControlCAsInput;
        Console.TreatControlCAsInput = true;
        try
        {
            while (true)
            {
                Render();
                var (ctrlC, input) = ReadLineOrCtrlC_Engine(
                    CommandHistory,
                    onRepaint: () =>
                    {
                        Render();
                        return (ConsoleEx.CursorLeft, ConsoleEx.CursorTop);
                    },
                    onPageUp: () => PageUp(),
                    onPageDown: () => PageDown());
                if (ctrlC)
                {
                    var quit = new Command.Quit();
                    var shouldExit = await quit.TryQuitAsync(this);
                    if (shouldExit) return;
                    // back to main screen
                    continue;
                }
                if (input is null) continue;
                if (!CommandParser.TryParse(input, out var cmd, out var err) || cmd is null)
                {
                    if (!string.IsNullOrEmpty(err))
                    {
                        Console.WriteLine(err);
                        Console.WriteLine("Press Enter to continue...");
                        Console.ReadLine();
                    }
                    continue;
                }
                var result = await cmd.ExecuteAsync(this);
                // Add executed command to history unless it is a duplicate of the last entry
                if (!string.IsNullOrWhiteSpace(input))
                {
                    var last = CommandHistory.Count > 0 ? CommandHistory[^1] : null;
                    if (!string.Equals(last, input, StringComparison.Ordinal))
                    {
                        CommandHistory.Add(input);
                    }
                }
                if (result.ShouldExit) return;
            }
        }
        finally
        {
            Console.TreatControlCAsInput = prevTreatCtrlC;
        }
    }

    // Command prompt line editor with history, cursor movement, word jumps, delete/backspace, ESC-to-clear, and instant Ctrl+C
    // Retired ReadLineOrCtrlC: replaced by engine-backed ReadLineOrCtrlC_Engine

    // Simpler input reader for command prompts: supports ESC/Ctrl+C cancel, PageUp/PageDown, full cursor/word ops, and viewport ellipses
    internal (bool Cancelled, string? Text) ReadLineWithPagingCancelable(
        Func<(int Left, int Top)> onRepaint,
        Action onPageUp,
        Action onPageDown,
        string? initial = null)
    {
        var engine = new LineEditorEngine();
        engine.SetInitial(initial ?? string.Empty);
        int startLeft, startTop;
        startLeft = ConsoleEx.CursorLeft; startTop = ConsoleEx.CursorTop;

        void Render()
        {
            int winWidth = Console.WindowWidth;
            int contentWidth = Math.Max(1, winWidth - startLeft - 1);
            engine.EnsureVisible(contentWidth);
            var view = engine.GetView(contentWidth);
            ConsoleEx.SetCursorPosition(startLeft, startTop);
            int vlen = Math.Min(view.Length, contentWidth);
            if (vlen > 0) Console.Write(view[..vlen]);
            if (vlen < contentWidth) Console.Write(new string(' ', contentWidth - vlen));
            int cursorCol = startLeft + Math.Min(engine.Cursor - engine.ScrollStart, contentWidth - 1);
            int safeCol = Math.Min(Math.Max(0, winWidth - 1), Math.Max(0, cursorCol));
            ConsoleEx.SetCursorPosition(safeCol, startTop);
        }

        Render();
        while (true)
        {
            ConsoleKeyInfo key;
            if (Console.KeyAvailable) key = Console.ReadKey(intercept: true); else { try { System.Threading.Thread.Sleep(25); } catch { } continue; }

            if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.C) { ConsoleEx.WriteLine(""); return (true, null); }
            if (key.Key == ConsoleKey.Escape) { ConsoleEx.WriteLine(""); return (true, null); }

            if (key.Key == ConsoleKey.PageUp || key.Key == ConsoleKey.PageDown)
            {
                try { if (key.Key == ConsoleKey.PageUp) onPageUp(); else onPageDown(); } catch { }
                try { var pos = onRepaint(); startLeft = pos.Left; startTop = pos.Top; } catch { }
                Render();
                continue;
            }

            if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.LeftArrow) { engine.CtrlWordLeft(); Render(); continue; }
            if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.RightArrow) { engine.CtrlWordRight(); Render(); continue; }
            if (((key.Modifiers & ConsoleModifiers.Control) != 0 || (key.Modifiers & ConsoleModifiers.Alt) != 0) && key.Key == ConsoleKey.Backspace) { engine.CtrlWordBackspace(); Render(); continue; }
            if (((key.Modifiers & ConsoleModifiers.Control) != 0 || (key.Modifiers & ConsoleModifiers.Alt) != 0) && key.Key == ConsoleKey.Delete) { engine.CtrlWordDelete(); Render(); continue; }

            if (key.Key == ConsoleKey.LeftArrow) { engine.Left(); Render(); continue; }
            if (key.Key == ConsoleKey.RightArrow) { engine.Right(); Render(); continue; }
            if (key.Key == ConsoleKey.Home) { engine.Home(); Render(); continue; }
            if (key.Key == ConsoleKey.End) { engine.End(); Render(); continue; }

            if (key.Key == ConsoleKey.Enter) { ConsoleEx.WriteLine(""); return (false, engine.Buffer.ToString()); }
            if (key.Key == ConsoleKey.Backspace) { engine.Backspace(); Render(); continue; }
            if (key.Key == ConsoleKey.Delete) { engine.Delete(); Render(); continue; }
            if (!char.IsControl(key.KeyChar)) { engine.Insert(key.KeyChar); Render(); continue; }
        }
    }

    // Engine-backed line editor for the main prompt with history + viewport + paging
    internal (bool CtrlC, string? Text) ReadLineOrCtrlC_Engine(
        List<string>? history = null,
        Func<(int Left, int Top)>? onRepaint = null,
        Action? onPageUp = null,
        Action? onPageDown = null)
    {
        var engine = new LineEditorEngine();
        engine.SetInitial(string.Empty);
        int startLeft, startTop;
        startLeft = ConsoleEx.CursorLeft; startTop = ConsoleEx.CursorTop;
        int lastW = ConsoleEx.WindowWidth, lastH = ConsoleEx.WindowHeight;

        int histIndex = history?.Count ?? 0; // bottom slot
        string draft = string.Empty;
        bool modifiedFromHistory = false;

        try
        {
            int avail = Math.Max(0, Console.WindowWidth - startLeft - 1);
            if (avail < 10) { Console.WriteLine(); startLeft = 0; startTop = Console.CursorTop; }
        }
        catch { }

        void Render()
        {
            int w = ConsoleEx.WindowWidth;
            int content = Math.Max(1, w - startLeft - 1);
            engine.EnsureVisible(content);
            var view = engine.GetView(content);
            try
            {
                Console.SetCursorPosition(startLeft, startTop);
                int vlen = Math.Min(view.Length, content);
                if (vlen > 0) Console.Write(view[..vlen]);
                if (vlen < content) Console.Write(new string(' ', content - vlen));
                int cursorCol = startLeft + Math.Min(engine.Cursor - engine.ScrollStart, content - 1);
                int safeCol = Math.Min(Math.Max(0, w - 1), Math.Max(0, cursorCol));
                Console.SetCursorPosition(safeCol, startTop);
            }
            catch { }
        }

        Render();
        while (true)
        {
            // Resize repaint
            try
            {
                int w = Console.WindowWidth, h = Console.WindowHeight;
                if ((w != lastW || h != lastH) && onRepaint is not null)
                {
                    lastW = w; lastH = h;
                    var pos = onRepaint();
                    startLeft = pos.Left; startTop = pos.Top;
                }
            }
            catch { }

            ConsoleKeyInfo key;
            if (Console.KeyAvailable) key = Console.ReadKey(intercept: true); else { try { System.Threading.Thread.Sleep(50); } catch { } continue; }

            if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.C) { ConsoleEx.WriteLine(""); return (true, null); }
            if (key.Key == ConsoleKey.Enter) { ConsoleEx.WriteLine(""); return (false, engine.Buffer.ToString()); }

            if (key.Key == ConsoleKey.PageUp || key.Key == ConsoleKey.PageDown)
            {
                try
                {
                    if (key.Key == ConsoleKey.PageUp) onPageUp?.Invoke(); else onPageDown?.Invoke();
                    if (onRepaint is not null) { var pos = onRepaint(); startLeft = pos.Left; startTop = pos.Top; }
                }
                catch { }
                Render();
                continue;
            }
            if (key.Key == ConsoleKey.Escape)
            {
                if (histIndex != (history?.Count ?? 0)) histIndex = history?.Count ?? 0;
                engine.SetInitial(string.Empty);
                draft = string.Empty;
                modifiedFromHistory = true;
                Render();
                continue;
            }

            // History navigation
            if (key.Key == ConsoleKey.UpArrow && history is not null)
            {
                if (histIndex > 0)
                {
                    if (histIndex == history.Count) draft = engine.Buffer.ToString();
                    histIndex--;
                    engine.SetInitial(history[histIndex]);
                    modifiedFromHistory = false;
                    Render();
                }
                continue;
            }
            if (key.Key == ConsoleKey.DownArrow && history is not null)
            {
                if (histIndex < history.Count)
                {
                    histIndex++;
                    if (histIndex == history.Count) engine.SetInitial(draft); else engine.SetInitial(history[histIndex]);
                    modifiedFromHistory = false;
                    Render();
                }
                continue;
            }

            void EnsureDraft()
            {
                if (histIndex != (history?.Count ?? 0) && !modifiedFromHistory)
                {
                    modifiedFromHistory = true;
                    draft = engine.Buffer.ToString();
                    histIndex = history?.Count ?? 0;
                }
            }

            // Word ops
            if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.LeftArrow) { engine.CtrlWordLeft(); Render(); continue; }
            if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.RightArrow) { engine.CtrlWordRight(); Render(); continue; }
            if (((key.Modifiers & ConsoleModifiers.Control) != 0 || (key.Modifiers & ConsoleModifiers.Alt) != 0) && key.Key == ConsoleKey.Backspace) { EnsureDraft(); engine.CtrlWordBackspace(); if (histIndex == (history?.Count ?? 0)) draft = engine.Buffer.ToString(); Render(); continue; }
            if (((key.Modifiers & ConsoleModifiers.Control) != 0 || (key.Modifiers & ConsoleModifiers.Alt) != 0) && key.Key == ConsoleKey.Delete) { EnsureDraft(); engine.CtrlWordDelete(); if (histIndex == (history?.Count ?? 0)) draft = engine.Buffer.ToString(); Render(); continue; }

            // Basic nav
            if (key.Key == ConsoleKey.LeftArrow) { engine.Left(); Render(); continue; }
            if (key.Key == ConsoleKey.RightArrow) { engine.Right(); Render(); continue; }
            if (key.Key == ConsoleKey.Home) { engine.Home(); Render(); continue; }
            if (key.Key == ConsoleKey.End) { engine.End(); Render(); continue; }

            // Char edits
            if (key.Key == ConsoleKey.Backspace) { EnsureDraft(); engine.Backspace(); if (histIndex == (history?.Count ?? 0)) draft = engine.Buffer.ToString(); Render(); continue; }
            if (key.Key == ConsoleKey.Delete) { EnsureDraft(); engine.Delete(); if (histIndex == (history?.Count ?? 0)) draft = engine.Buffer.ToString(); Render(); continue; }
            if (!char.IsControl(key.KeyChar)) { EnsureDraft(); engine.Insert(key.KeyChar); if (histIndex == (history?.Count ?? 0)) draft = engine.Buffer.ToString(); Render(); continue; }
        }
    }

    private static string MakeKey(string fullKey, string? label)
        => fullKey + "\n" + (label ?? string.Empty);

    internal async Task SaveAsync(bool pause = true)
    {
        Console.WriteLine("Saving changes...");
        int changes = 0;

        // Compute consolidated change set using Core.ChangeApplier
        var mapper = new EditorMappers();
        var coreItems = Items.Select(mapper.ToCoreItem).ToList();
        var changeSet = AppConfigCli.Core.ChangeApplier.Compute(coreItems);

        // Apply upserts (last-wins per key/label already handled in ChangeApplier)
        foreach (var up in changeSet.Upserts)
        {
            try
            {
                await _repo.UpsertAsync(up);

                // Mark all corresponding UI items as unchanged and sync OriginalValue
                foreach (var it in Items.Where(i =>
                    i.FullKey == up.Key &&
                    string.Equals(AppConfigCli.Core.LabelFilter.ForWrite(i.Label), up.Label, StringComparison.Ordinal)).ToList())
                {
                    it.OriginalValue = it.Value;
                    it.State = ItemState.Unchanged;
                }
                changes++;
            }
            catch (RequestFailedException ex)
            {
                Console.WriteLine($"Failed to set '{up.Key}': {ex.Message}");
            }
        }

        // Apply deletions
        foreach (var del in changeSet.Deletes)
        {
            try
            {
                await _repo.DeleteAsync(del.Key, del.Label);
                // Remove only items marked as Deleted for that key/label
                for (int idx = Items.Count - 1; idx >= 0; idx--)
                {
                    var it = Items[idx];
                    if (it.State != ItemState.Deleted) continue;
                    if (it.FullKey != del.Key) continue;
                    if (!string.Equals(AppConfigCli.Core.LabelFilter.ForWrite(it.Label), del.Label, StringComparison.Ordinal)) continue;
                    Items.RemoveAt(idx);
                }
                changes++;
            }
            catch (RequestFailedException ex)
            {
                Console.WriteLine($"Failed to delete '{del.Key}': {ex.Message}");
            }
        }

        Console.WriteLine(changes == 0 ? "No changes to save." : $"Saved {changes} change(s).");
        if (pause)
        {
            Console.WriteLine("Press Enter to continue...");
            Console.ReadLine();
        }
    }

    internal bool HasPendingChanges(out int newCount, out int modCount, out int delCount)
    {
        newCount = Items.Count(i => i.State == ItemState.New);
        modCount = Items.Count(i => i.State == ItemState.Modified);
        delCount = Items.Count(i => i.State == ItemState.Deleted);
        return (newCount + modCount + delCount) > 0;
    }

    internal static int CompareItems(Item a, Item b)
    {
        //TODO: This utility function should be moved somewhere else
        int c = string.Compare(a.ShortKey, b.ShortKey, StringComparison.Ordinal);
        if (c != 0) return c;
        return string.Compare(a.Label ?? string.Empty, b.Label ?? string.Empty, StringComparison.Ordinal);
    }

    internal void ConsolidateDuplicates()
    {
        var groups = Items.GroupBy(i => MakeKey(i.FullKey, i.Label)).ToList();
        foreach (var g in groups)
        {
            if (g.Count() <= 1) continue;
            var keep = g.FirstOrDefault(i => i.State != ItemState.Deleted) ?? g.First();
            foreach (var extra in g)
            {
                if (!ReferenceEquals(extra, keep))
                {
                    Items.Remove(extra);
                }
            }
        }
    }

    internal List<Item> GetVisibleItems()
    {
        // Delegate visibility to Core.ItemFilter to keep semantics centralized
        var mapper = new EditorMappers();
        var coreList = Items.Select(mapper.ToCoreItem).ToList();
        var indices = AppConfigCli.Core.ItemFilter.VisibleIndices(coreList, Label, KeyRegex);
        var result = new List<Item>(indices.Count);
        foreach (var idx in indices)
        {
            result.Add(Items[idx]);
        }
        return result;
    }

    // (obsolete nested tree helpers removed; structured editors use Core.FlatKeyMapper)

    internal List<int>? MapVisibleRangeToItemIndices(int start, int end, out string error)
    {
        // Use Core.ItemFilter to compute indices against a mapped Core list
        var mapper = new EditorMappers();
        var coreList = Items.Select(mapper.ToCoreItem).ToList();
        var indices = AppConfigCli.Core.ItemFilter.MapVisibleRangeToSourceIndices(coreList, Label, KeyRegex, start, end, out error);
        return indices;
    }

    // Cached prefix candidates built from all repository keys plus current in-memory items
    private List<string>? _prefixCache;
    internal void InvalidatePrefixCache()
    {
        _prefixCache = null;
    }

    internal async Task<IReadOnlyList<string>> GetPrefixCandidatesAsync()
    {
        if (_prefixCache is not null)
            return _prefixCache;

        var set = new HashSet<string>(StringComparer.Ordinal);

        // Include in-memory items (unsaved/new)
        foreach (var it in Items)
        {
            if (it.FullKey != null)
            {
                var index = it.FullKey.IndexOf('/');
                if (index > 0) set.Add(it.FullKey[..(index + 1)]);
            }
        }

        // Include all repository entries (ignoring filters)
        //TODO: We could filter by Label, if we invalidate the cache on label change
        var allKeys = await _repo.FetchKeysAsync(prefix: null, labelFilter: null).ConfigureAwait(false);
        foreach (var key in allKeys)
        {
            var index = key.IndexOf('/');
            if (index > 0)
            {
                set.Add(key[..(index + 1)]);
            }
        }

        _prefixCache = [.. set.OrderBy(s => s, StringComparer.Ordinal)];
        return _prefixCache;
    }

    internal bool TryAddPrefixFromKey(string key)
    {
        if (_prefixCache is null)
            return false; // cache not built yet

        if (string.IsNullOrEmpty(key))
            return false; // no key => no prefix

        var keyIndex = key.IndexOf('/');
        if (keyIndex <= 0)
            return false; // no prefix

        var prefix = key[..(keyIndex + 1)];

        var prefixIndex = _prefixCache.BinarySearch(prefix, StringComparer.Ordinal);
        if (prefixIndex >= 0)
            return false; // already present

        // insert prefix in sorted order
        _prefixCache.Insert(~prefixIndex, prefix);
        return true;
    }
}
