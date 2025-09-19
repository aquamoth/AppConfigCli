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
        var options = ParseArgs(args);
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
        var app = new EditorApp(repo, options.Prefix, options.Label, whoAmIAction, authModeDesc);
        try
        {
            // Helpers to construct nested structure with objects/arrays
            void AddPathDict(Dictionary<string, object> node, string[] segments, string value)
            {
                if (segments.Length == 0)
                {
                    node["__value"] = value;
                    return;
                }
                var head = segments[0];
                if (!node.TryGetValue(head, out var child))
                {
                    if (segments.Length == 1)
                    {
                        node[head] = value;
                        return;
                    }
                    bool nextIsIndex = int.TryParse(segments[1], out _);
                    if (nextIsIndex)
                    {
                        var list = new List<object?>();
                        node[head] = list;
                        AddPathList(list, segments[1..], value);
                    }
                    else
                    {
                        var dict = new Dictionary<string, object>(StringComparer.Ordinal);
                        node[head] = dict;
                        AddPathDict(dict, segments[1..], value);
                    }
                }
                else if (child is string s)
                {
                    bool nextIsIndex = segments.Length > 1 && int.TryParse(segments[1], out _);
                    if (nextIsIndex)
                    {
                        var list = new List<object?>();
                        node[head] = list;
                        AddPathList(list, segments[1..], value);
                    }
                    else
                    {
                        var dict = new Dictionary<string, object>(StringComparer.Ordinal) { ["__value"] = s };
                        node[head] = dict;
                        AddPathDict(dict, segments[1..], value);
                    }
                }
                else if (child is Dictionary<string, object> dict)
                {
                    if (segments.Length == 1)
                    {
                        dict["__value"] = value;
                    }
                    else
                    {
                        AddPathDict(dict, segments[1..], value);
                    }
                }
                else if (child is List<object?> list)
                {
                    AddPathList(list, segments[1..], value);
                }
            }

            void AddPathList(List<object?> list, string[] segments, string value)
            {
                if (segments.Length == 0) return; // nothing to set
                var idxStr = segments[0];
                if (!int.TryParse(idxStr, out int idx))
                {
                    // Treat non-numeric as object under the array element
                    EnsureListSize(list, 1);
                    var head = 0; // fallback index
                    if (list[head] is not Dictionary<string, object> d)
                    {
                        d = new Dictionary<string, object>(StringComparer.Ordinal);
                        list[head] = d;
                    }
                    AddPathDict(d, segments, value);
                    return;
                }
                EnsureListSize(list, idx + 1);
                var child = list[idx];
                if (segments.Length == 1)
                {
                    list[idx] = value;
                    return;
                }
                bool nextIsIndex = int.TryParse(segments[1], out _);
                if (child is null)
                {
                    if (nextIsIndex)
                    {
                        var inner = new List<object?>();
                        list[idx] = inner;
                        AddPathList(inner, segments[1..], value);
                    }
                    else
                    {
                        var d = new Dictionary<string, object>(StringComparer.Ordinal);
                        list[idx] = d;
                        AddPathDict(d, segments[1..], value);
                    }
                }
                else if (child is string s)
                {
                    if (nextIsIndex)
                    {
                        var inner = new List<object?>();
                        list[idx] = inner;
                        AddPathList(inner, segments[1..], value);
                    }
                    else
                    {
                        var d = new Dictionary<string, object>(StringComparer.Ordinal) { ["__value"] = s };
                        list[idx] = d;
                        AddPathDict(d, segments[1..], value);
                    }
                }
                else if (child is Dictionary<string, object> d)
                {
                    AddPathDict(d, segments[1..], value);
                }
                else if (child is List<object?> l)
                {
                    AddPathList(l, segments[1..], value);
                }
            }

            void EnsureListSize(List<object?> list, int size)
            {
                while (list.Count < size) list.Add(null);
            }
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
        Console.WriteLine("Usage: appconfig [--prefix <keyPrefix>] [--label <label>] [--endpoint <url>] [--tenant <guid>] [--auth <mode>]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --prefix <value>    Optional. Key prefix (section) to load initially");
        Console.WriteLine("  --label <value>     Optional. Label filter");
        Console.WriteLine("  --endpoint <url>    Optional. App Configuration endpoint for AAD auth");
        Console.WriteLine("  --tenant <guid>     Optional. Entra ID tenant for AAD auth");
        Console.WriteLine("  --auth <mode>       Optional. Auth method: auto|device|browser|cli|vscode (default: auto)");
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
    }
}

// UI models moved to Editor/UiModels.cs

internal sealed partial class EditorApp
{
    private readonly AppConfigCli.Core.IConfigRepository _repo;
    private string? _prefix;
    private string? _label;
    private readonly List<Item> _items = new();
    private readonly Func<Task>? _whoAmI;
    private readonly string _authModeDesc;
    private string? _keyRegexPattern;
    private Regex? _keyRegex;
    private readonly IFileSystem _fs;
    private readonly IExternalEditor _externalEditor;

    public EditorApp(AppConfigCli.Core.IConfigRepository repo, string? prefix, string? label, Func<Task>? whoAmI = null, string authModeDesc = "", IFileSystem? fs = null, IExternalEditor? externalEditor = null)
    {
        _repo = repo;
        _prefix = prefix;
        _label = label;
        _whoAmI = whoAmI;
        _authModeDesc = authModeDesc;
        _fs = fs ?? new DefaultFileSystem();
        _externalEditor = externalEditor ?? new DefaultExternalEditor();
    }

    public async Task LoadAsync()
    {
        // Build server snapshot
        var server = (await _repo.ListAsync(_prefix, _label)).ToList();

        // Map local items to Core using Mapperly
        var mapper = new EditorMappers();
        var local = _items.Select(mapper.ToCoreItem).ToList();

        var reconciler = new AppStateReconciler();
        var freshCore = reconciler.Reconcile(_prefix ?? string.Empty, _label, local, server);

        _items.Clear();
        foreach (var it in freshCore)
        {
            _items.Add(mapper.ToUiItem(it));
        }
    }

    public async Task RunAsync()
    {
        while (true)
        {
            Render();
            Console.Write("> ");
            var input = Console.ReadLine();
            if (input is null) continue;
            var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            var cmd = parts[0].ToLowerInvariant();

            switch (cmd)
            {
                case "e":
                case "edit":
                    Edit(parts.Skip(1).ToArray());
                    break;
                case "a":
                case "add":
                    Add();
                    break;
                case "c":
                case "copy":
                    await CopyAsync(parts.Skip(1).ToArray());
                    break;
                case "p":
                case "prefix":
                    await ChangePrefixAsync(parts.Skip(1).ToArray());
                    break;
                case "d":
                case "delete":
                    Delete(parts.Skip(1).ToArray());
                    break;
                case "u":
                case "undo":
                    Undo(parts.Skip(1).ToArray());
                    break;
                case "g":
                case "grep":
                    SetKeyRegex(parts.Skip(1).ToArray());
                    break;
                case "json":
                    await OpenJsonInEditorAsync(parts.Skip(1).ToArray());
                    break;
                case "yaml":
                    await OpenYamlInEditorAsync(parts.Skip(1).ToArray());
                    break;
                case "label":
                case "l":
                    await ChangeLabelAsync(parts.Skip(1).ToArray());
                    break;
                case "s":
                case "save":
                    await SaveAsync();
                    break;
                case "r":
                case "reload":
                    await LoadAsync();
                    break;
                case "q":
                case "quit":
                case "exit":
                    if (HasPendingChanges(out var newCount, out var modCount, out var delCount))
                    {
                        Console.WriteLine($"You have unsaved changes: +{newCount} new, *{modCount} modified, -{delCount} deleted.");
                        Console.WriteLine("Do you want to save before exiting?");
                        Console.WriteLine("  S) Save and quit");
                        Console.WriteLine("  Q) Quit without saving");
                        Console.WriteLine("  C) Cancel");
                        while (true)
                        {
                            Console.Write("> ");
                            var choice = (Console.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();
                            if (choice.Length == 0) continue;
                            var ch = choice[0];
                            if (ch == 'c') break; // cancel quit
                            if (ch == 's') { await SaveAsync(pause: false); return; }
                            if (ch == 'q') { return; }
                            Console.WriteLine("Please enter S, Q, or C.");
                        }
                        break;
                    }
                    else
                    {
                        return;
                    }
                case "h":
                case "?":
                case "help":
                    ShowHelp();
                    break;
                case "whoami":
                case "w":
                    if (_whoAmI is not null) await _whoAmI(); else Console.WriteLine("whoami not available in this mode.");
                    Console.WriteLine("Press Enter to continue...");
                    Console.ReadLine();
                    break;
                case "o":
                case "open":
                    await OpenInEditorAsync();
                    break;
                default:
                    Console.WriteLine("Unknown command. Type 'h' for help.");
                    break;
            }
        }
    }

    private static string MakeKey(string fullKey, string? label)
        => fullKey + "\n" + (label ?? string.Empty);

    private static void SplitKey(string composite, out string fullKey, out string? label)
    {
        var idx = composite.IndexOf('\n');
        if (idx < 0) { fullKey = composite; label = null; return; }
        fullKey = composite[..idx];
        var rest = composite[(idx + 1)..];
        label = rest.Length == 0 ? null : rest;
    }

    

    // Render moved to Editor/EditorApp.UI.cs

    // ShowHelp moved to Editor/EditorApp.UI.cs

    private void Edit(string[] args)
    {
        if (!TryParseIndex(args, out var idx)) return;
        var item = _items[idx];
        var label = item.Label ?? "(none)";
        Console.WriteLine($"Editing '{item.ShortKey}' [{label}]  (Enter to save)");
        Console.Write("> ");
        var newVal = ReadLineWithInitial(item.Value ?? string.Empty);
        if (newVal is null) return;
        item.Value = newVal;
        if (!item.IsNew && item.Value != item.OriginalValue)
            item.State = ItemState.Modified;
        if (!item.IsNew && item.Value == item.OriginalValue)
            item.State = ItemState.Unchanged;
    }

    private void Add()
    {
        Console.WriteLine("Enter new key (relative to prefix):");
        Console.Write("> ");
        var k = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(k)) return;
        k = k.Trim();
        
        string? chosenLabel = _label;
        if (chosenLabel is not null)
        {
            Console.WriteLine($"Adding new key under label: [{chosenLabel}]");
        }
        else
        {
            Console.WriteLine("Enter label for new key (empty for none):");
            Console.Write("> ");
            var lbl = Console.ReadLine();
            chosenLabel = string.IsNullOrWhiteSpace(lbl) ? null : lbl!.Trim();
            Console.WriteLine($"Using label: [{chosenLabel ?? "(none)"}]");
        }

        // Prevent duplicates only for the same label
        if (_items.Any(i => i.ShortKey.Equals(k, StringComparison.Ordinal)
                            && string.Equals(i.Label ?? string.Empty, chosenLabel ?? string.Empty, StringComparison.Ordinal)))
        {
            Console.WriteLine("Key already exists for this label.");
            return;
        }

        Console.WriteLine("Enter value:");
        Console.Write("> ");
        var v = Console.ReadLine() ?? string.Empty;
        var basePrefix = _prefix ?? string.Empty;
        _items.Add(new Item
        {
            FullKey = basePrefix + k,
            ShortKey = k,
            Label = chosenLabel,
            OriginalValue = null,
            Value = v,
            State = ItemState.New
        });
        _items.Sort(CompareItems);
    }

    private async Task CopyAsync(string[] args)
    {
        if (_label is null)
        {
            Console.WriteLine("Copy requires an active label filter. Set one with l|label <value> first.");
            Console.WriteLine("Press Enter to continue...");
            Console.ReadLine();
            return;
        }

        if (args.Length == 0)
        {
            Console.WriteLine("Usage: c|copy <n> [m]  (copies rows n..m)");
            Console.WriteLine("Press Enter to continue...");
            Console.ReadLine();
            return;
        }

        if (!int.TryParse(args[0], out int start)) { Console.WriteLine("First argument must be an index."); Console.ReadLine(); return; }
        int end = start;
        if (args.Length >= 2 && int.TryParse(args[1], out int endParsed)) end = endParsed;
        var actualIndices = MapVisibleRangeToItemIndices(start, end, out var error);
        if (actualIndices is null) { Console.WriteLine(error); Console.ReadLine(); return; }

        var selection = new List<(string ShortKey, string Value)>();
        foreach (var idx in actualIndices)
        {
            var it = _items[idx];
            if (it.State == ItemState.Deleted) continue;
            var val = it.Value ?? string.Empty;
            selection.Add((it.ShortKey, val));
        }
        if (selection.Count == 0)
        {
            Console.WriteLine("Nothing to copy in the selected range."); Console.ReadLine(); return;
        }

        Console.WriteLine("Copy to label (empty for none):");
        Console.Write("> ");
        var target = Console.ReadLine();
        string? targetLabel = string.IsNullOrWhiteSpace(target) ? null : target!.Trim();

        // Switch to target label and load items for that label
        _label = targetLabel;
        await LoadAsync();

        int created = 0, updated = 0;
        foreach (var (shortKey, value) in selection)
        {
            var existing = _items.FirstOrDefault(x => x.ShortKey.Equals(shortKey, StringComparison.Ordinal));
            if (existing is null)
            {
                _items.Add(new Item
                {
                    FullKey = _prefix + shortKey,
                    ShortKey = shortKey,
                    Label = targetLabel,
                    OriginalValue = null,
                    Value = value,
                    State = ItemState.New
                });
                created++;
            }
            else
            {
                existing.Value = value;
                if (existing.OriginalValue == value)
                    existing.State = ItemState.Unchanged;
                else if (!existing.IsNew)
                    existing.State = ItemState.Modified;
                updated++;
            }
        }

        _items.Sort(CompareItems);
        Console.WriteLine($"Copied {selection.Count} item(s): {created} created, {updated} updated under label [{targetLabel ?? "(none)"}].");
        Console.WriteLine("Press Enter to continue...");
        Console.ReadLine();
    }

    private void Delete(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: d|delete <n> [m]  (deletes rows n..m)");
            Console.WriteLine("Press Enter to continue...");
            Console.ReadLine();
            return;
        }

        if (!int.TryParse(args[0], out int start)) { Console.WriteLine("First argument must be an index."); Console.ReadLine(); return; }
        int end = start;
        if (args.Length >= 2 && int.TryParse(args[1], out int endParsed)) end = endParsed;
        var actualIndices = MapVisibleRangeToItemIndices(start, end, out var error);
        if (actualIndices is null) { Console.WriteLine(error); Console.ReadLine(); return; }

        int removedNew = 0, markedExisting = 0;
        foreach (var idx in actualIndices.OrderByDescending(i => i))
        {
            var item = _items[idx];
            if (item.IsNew)
            {
                _items.RemoveAt(idx);
                removedNew++;
            }
            else
            {
                item.State = ItemState.Deleted;
                markedExisting++;
            }
        }

        Console.WriteLine($"Deleted selection: removed {removedNew} new item(s), marked {markedExisting} existing item(s) for deletion.");
        Console.WriteLine("Press Enter to continue...");
        Console.ReadLine();
    }

    private void Undo(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: u|undo <n> [m]  (undos rows n..m)");
            Console.WriteLine("Press Enter to continue...");
            Console.ReadLine();
            return;
        }

        if (args.Length == 1 && string.Equals(args[0], "all", StringComparison.OrdinalIgnoreCase))
        {
            UndoAll();
            return;
        }

        if (!int.TryParse(args[0], out int start)) { Console.WriteLine("First argument must be an index or 'all'."); Console.ReadLine(); return; }
        int end = start;
        if (args.Length >= 2 && int.TryParse(args[1], out int endParsed)) end = endParsed;
        var actualIndices = MapVisibleRangeToItemIndices(start, end, out var error);
        if (actualIndices is null) { Console.WriteLine(error); Console.ReadLine(); return; }

        int removedNew = 0, restored = 0, untouched = 0;
        foreach (var idx in actualIndices.OrderByDescending(i => i))
        {
            if (idx < 0 || idx >= _items.Count) { continue; }
            var item = _items[idx];
            if (item.IsNew)
            {
                _items.RemoveAt(idx);
                removedNew++;
            }
            else if (item.State == ItemState.Deleted)
            {
                item.State = ItemState.Unchanged;
                restored++;
            }
            else if (item.State == ItemState.Modified)
            {
                item.Value = item.OriginalValue;
                item.State = ItemState.Unchanged;
                restored++;
            }
            else
            {
                untouched++;
            }
        }

        Console.WriteLine($"Undo selection: removed {removedNew} new item(s), restored {restored} item(s), untouched {untouched}.");
        Console.WriteLine("Press Enter to continue...");
        Console.ReadLine();
    }

    private void UndoAll()
    {
        int removedNew = 0, restored = 0, untouched = 0;
        // Iterate descending to safely remove new items
        for (int idx = _items.Count - 1; idx >= 0; idx--)
        {
            var item = _items[idx];
            if (item.IsNew)
            {
                _items.RemoveAt(idx);
                removedNew++;
            }
            else if (item.State == ItemState.Deleted)
            {
                item.State = ItemState.Unchanged;
                restored++;
            }
            else if (item.State == ItemState.Modified)
            {
                item.Value = item.OriginalValue;
                item.State = ItemState.Unchanged;
                restored++;
            }
            else
            {
                untouched++;
            }
        }

        Console.WriteLine($"Undo all: removed {removedNew} new item(s), restored {restored} item(s), untouched {untouched}.");
        Console.WriteLine("Press Enter to continue...");
        Console.ReadLine();
    }

    private bool TryParseIndex(string[] args, out int idx)
    {
        idx = -1;
        if (args.Length == 0 || !int.TryParse(args[0], out var n))
        {
            Console.WriteLine("Provide an item number.");
            return false;
        }
        n -= 1;
        var vis = GetVisibleItems();
        if (n < 0 || n >= vis.Count)
        {
            Console.WriteLine("Invalid item number.");
            return false;
        }
        var target = vis[n];
        idx = _items.IndexOf(target);
        return true;
    }

    private async Task ChangeLabelAsync(string[] args)
    {
        if (args.Length == 0)
        {
            // No argument clears the filter (any label)
            _label = null;
            await LoadAsync();
            return;
        }

        var newLabelArg = string.Join(' ', args).Trim();
        if (newLabelArg == "-")
        {
            // Single dash selects the explicitly empty label
            _label = string.Empty;
        }
        else
        {
            // Any other value is a literal label
            _label = newLabelArg;
        }
        await LoadAsync();
    }

    private async Task ChangePrefixAsync(string[] args)
    {
        string? newPrefix = null;
        if (args.Length == 0)
        {
            Console.WriteLine("Enter new prefix (empty for all keys):");
            Console.Write("> ");
            var input = Console.ReadLine();
            newPrefix = input is null ? string.Empty : input.Trim();
        }
        else
        {
            newPrefix = string.Join(' ', args).Trim();
        }

        if (HasPendingChanges(out var newCount, out var modCount, out var delCount))
        {
            Console.WriteLine($"You have unsaved changes: +{newCount} new, *{modCount} modified, -{delCount} deleted.");
            Console.WriteLine("Change prefix now?");
            Console.WriteLine("  S) Save and change");
            Console.WriteLine("  Q) Change without saving (discard)");
            Console.WriteLine("  C) Cancel");
            while (true)
            {
                Console.Write("> ");
                var choice = (Console.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();
                if (choice.Length == 0) continue;
                var ch = choice[0];
                if (ch == 'c') return;
                if (ch == 's') { await SaveAsync(pause: false); break; }
                if (ch == 'q') { /* discard */ break; }
                Console.WriteLine("Please enter S, Q, or C.");
            }
        }

        _prefix = newPrefix; // can be empty string to mean 'all keys'
        await LoadAsync();
    }

    private async Task SaveAsync(bool pause = true)
    {
        Console.WriteLine("Saving changes...");
        int changes = 0;

        // Compute consolidated change set using Core.ChangeApplier
        var mapper = new EditorMappers();
        var coreItems = _items.Select(mapper.ToCoreItem).ToList();
        var changeSet = AppConfigCli.Core.ChangeApplier.Compute(coreItems);

        // Apply upserts (last-wins per key/label already handled in ChangeApplier)
        foreach (var up in changeSet.Upserts)
        {
            try
            {
                await _repo.UpsertAsync(up);

                // Mark all corresponding UI items as unchanged and sync OriginalValue
                foreach (var it in _items.Where(i =>
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
                for (int idx = _items.Count - 1; idx >= 0; idx--)
                {
                    var it = _items[idx];
                    if (it.State != ItemState.Deleted) continue;
                    if (it.FullKey != del.Key) continue;
                    if (!string.Equals(AppConfigCli.Core.LabelFilter.ForWrite(it.Label), del.Label, StringComparison.Ordinal)) continue;
                    _items.RemoveAt(idx);
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

    private async Task OpenInEditorAsync()
    {
        if (_label is null)
        {
            Console.WriteLine("Open requires an active label filter. Set one with l|label <value> first.");
            Console.WriteLine("Press Enter to continue...");
            Console.ReadLine();
            return;
        }

        // Prepare temp file
        string tmpDir = _fs.GetTempPath();
        string file = _fs.Combine(tmpDir, $"appconfig-{Guid.NewGuid():N}.txt");

        try
        {
            var content = BulkEditHelper.BuildInitialFileContent(GetVisibleItems().Where(i => i.State != ItemState.Deleted), _prefix, _label);
            _fs.WriteAllText(file, content);

            // Launch editor
            try
            {
                _externalEditor.Open(file);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to launch editor: {ex.Message}");
                Console.WriteLine("Press Enter to continue...");
                Console.ReadLine();
                return;
            }

            // Read back and reconcile
            var lines = _fs.ReadAllLines(file);
            var fileText = string.Join("\n", lines);
            var visibleUnderLabel = GetVisibleItems();
            var (created, updated, deleted) = BulkEditHelper.ApplyEdits(fileText, _items, visibleUnderLabel, _prefix, _label);
            ConsolidateDuplicates();
            _items.Sort(CompareItems);
            Console.WriteLine($"Bulk edit applied for label [{_label ?? "(none)"}]: {created} added, {updated} updated, {deleted} deleted.");
            Console.WriteLine("Press Enter to continue...");
            Console.ReadLine();
        }
        finally
        {
            _fs.Delete(file);
        }
    }

    private async Task OpenJsonInEditorAsync(string[] args)
    {
        if (_label is null)
        {
            Console.WriteLine("json requires an active label filter. Set one with l|label <value> first.");
            Console.WriteLine("Press Enter to continue...");
            Console.ReadLine();
            return;
        }

        if (args.Length == 0)
        {
            Console.WriteLine("Usage: json <separator>   e.g., json :");
            Console.WriteLine("Press Enter to continue...");
            Console.ReadLine();
            return;
        }

        var sep = string.Join(' ', args);
        if (string.IsNullOrEmpty(sep))
        {
            Console.WriteLine("Separator cannot be empty.");
            Console.WriteLine("Press Enter to continue...");
            Console.ReadLine();
            return;
        }

        string tmpDir = _fs.GetTempPath();
        string file = _fs.Combine(tmpDir, $"appconfig-json-{Guid.NewGuid():N}.json");

        try
        {
            var json = StructuredEditHelper.BuildJsonContent(GetVisibleItems(), sep);
            _fs.WriteAllText(file, json);

            // Launch editor
            try { _externalEditor.Open(file); }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to launch editor: {ex.Message}");
                Console.WriteLine("Press Enter to continue...");
                Console.ReadLine();
                return;
            }

            // Apply edits via StructuredEditHelper and return
            var editedJson = string.Join("\n", _fs.ReadAllLines(file));
            var (ok, err, cJ, uJ, dJ) = StructuredEditHelper.ApplyJsonEdits(editedJson, sep, _items, GetVisibleItems(), _prefix, _label);
            if (!ok)
            {
                Console.WriteLine($"Invalid JSON: {err}");
                Console.WriteLine("Press Enter to continue...");
                Console.ReadLine();
                return;
            }
            ConsolidateDuplicates();
            _items.Sort(CompareItems);
            Console.WriteLine($"JSON edit applied for label [{(_label?.Length == 0 ? "(none)" : _label) ?? "(any)"}]: {cJ} added, {uJ} updated, {dJ} deleted.");
            Console.WriteLine("Press Enter to continue...");
            Console.ReadLine();
            return;

            
        }
        finally
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
        }
    }

    private async Task OpenYamlInEditorAsync(string[] args)
    {
        if (_label is null)
        {
            Console.WriteLine("yaml requires an active label filter. Set one with l|label <value> first.");
            Console.WriteLine("Press Enter to continue...");
            Console.ReadLine();
            return;
        }

        if (args.Length == 0)
        {
            Console.WriteLine("Usage: yaml <separator>   e.g., yaml :");
            Console.WriteLine("Press Enter to continue...");
            Console.ReadLine();
            return;
        }

        var sep = string.Join(' ', args);
        if (string.IsNullOrEmpty(sep))
        {
            Console.WriteLine("Separator cannot be empty.");
            Console.WriteLine("Press Enter to continue...");
            Console.ReadLine();
            return;
        }

        string tmpDir = _fs.GetTempPath();
        string file = _fs.Combine(tmpDir, $"appconfig-yaml-{Guid.NewGuid():N}.yaml");

        try
        {
            void AddPathDict(Dictionary<string, object> node, string[] segments, string value)
            {
                if (segments.Length == 0)
                {
                    node["__value"] = value;
                    return;
                }
                var head = segments[0];
                if (!node.TryGetValue(head, out var child))
                {
                    if (segments.Length == 1)
                    {
                        node[head] = value;
                        return;
                    }
                    bool nextIsIndex = int.TryParse(segments[1], out _);
                    if (nextIsIndex)
                    {
                        var list = new List<object?>();
                        node[head] = list;
                        AddPathList(list, segments[1..], value);
                    }
                    else
                    {
                        var dict = new Dictionary<string, object>(StringComparer.Ordinal);
                        node[head] = dict;
                        AddPathDict(dict, segments[1..], value);
                    }
                }
                else if (child is string s)
                {
                    bool nextIsIndex = segments.Length > 1 && int.TryParse(segments[1], out _);
                    if (nextIsIndex)
                    {
                        var list = new List<object?>();
                        node[head] = list;
                        AddPathList(list, segments[1..], value);
                    }
                    else
                    {
                        var dict = new Dictionary<string, object>(StringComparer.Ordinal) { ["__value"] = s };
                        node[head] = dict;
                        AddPathDict(dict, segments[1..], value);
                    }
                }
                else if (child is Dictionary<string, object> dict)
                {
                    if (segments.Length == 1)
                    {
                        dict["__value"] = value;
                    }
                    else
                    {
                        AddPathDict(dict, segments[1..], value);
                    }
                }
                else if (child is List<object?> list)
                {
                    AddPathList(list, segments[1..], value);
                }
            }

            void AddPathList(List<object?> list, string[] segments, string value)
            {
                if (segments.Length == 0) return; // nothing to set
                var idxStr = segments[0];
                if (!int.TryParse(idxStr, out int idx))
                {
                    // Treat non-numeric as object under the array element
                    EnsureListSize(list, 1);
                    var head = 0; // fallback index
                    if (list[head] is not Dictionary<string, object> d)
                    {
                        d = new Dictionary<string, object>(StringComparer.Ordinal);
                        list[head] = d;
                    }
                    AddPathDict(d, segments, value);
                    return;
                }
                EnsureListSize(list, idx + 1);
                var child = list[idx];
                if (segments.Length == 1)
                {
                    list[idx] = value;
                    return;
                }
                bool nextIsIndex = int.TryParse(segments[1], out _);
                if (child is null)
                {
                    if (nextIsIndex)
                    {
                        var inner = new List<object?>();
                        list[idx] = inner;
                        AddPathList(inner, segments[1..], value);
                    }
                    else
                    {
                        var d = new Dictionary<string, object>(StringComparer.Ordinal);
                        list[idx] = d;
                        AddPathDict(d, segments[1..], value);
                    }
                }
                else if (child is string s)
                {
                    if (nextIsIndex)
                    {
                        var inner = new List<object?>();
                        list[idx] = inner;
                        AddPathList(inner, segments[1..], value);
                    }
                    else
                    {
                        var d = new Dictionary<string, object>(StringComparer.Ordinal) { ["__value"] = s };
                        list[idx] = d;
                        AddPathDict(d, segments[1..], value);
                    }
                }
                else if (child is Dictionary<string, object> d)
                {
                    AddPathDict(d, segments[1..], value);
                }
                else if (child is List<object?> l)
                {
                    AddPathList(l, segments[1..], value);
                }
            }

            void EnsureListSize(List<object?> list, int size)
            {
                while (list.Count < size) list.Add(null);
            }

            // Build flats and map to nested tree via FlatKeyMapper
            var flats = GetVisibleItems()
                .Where(i => i.State != ItemState.Deleted)
                .ToDictionary(i => i.ShortKey, i => i.Value ?? string.Empty, StringComparer.Ordinal);
            var root = AppConfigCli.Core.FlatKeyMapper.BuildTree(flats, sep);
            var serializer = new SerializerBuilder().Build();
            var yaml = serializer.Serialize(root);
            File.WriteAllText(file, yaml);

            // Launch editor
            try { _externalEditor.Open(file); }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to launch editor: {ex.Message}");
                Console.WriteLine("Press Enter to continue...");
                Console.ReadLine();
                return;
            }

            

            // Apply edits via StructuredEditHelper and return
            var editedYaml = string.Join("\n", _fs.ReadAllLines(file));
            var (ok2, err2, c2, u2, d2) = StructuredEditHelper.ApplyYamlEdits(editedYaml, sep, _items, GetVisibleItems(), _prefix, _label);
            if (!ok2)
            {
                Console.WriteLine($"Invalid YAML: {err2}");
                Console.WriteLine("Press Enter to continue...");
                Console.ReadLine();
                return;
            }
            ConsolidateDuplicates();
            _items.Sort(CompareItems);
            Console.WriteLine($"YAML edit applied for label [{(_label?.Length == 0 ? "(none)" : _label) ?? "(any)"}]: {c2} added, {u2} updated, {d2} deleted.");
            Console.WriteLine("Press Enter to continue...");
            Console.ReadLine();
            return;

            
        }
        finally
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
        }
    }

    private static string EscapeValue(string value)
        => value.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n");

    private static string UnescapeValue(string value)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '\\' && i + 1 < value.Length)
            {
                char n = value[i + 1];
                if (n == 'n') { sb.Append('\n'); i++; continue; }
                if (n == 't') { sb.Append('\t'); i++; continue; }
                sb.Append(n); i++; continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static (string exe, string argsFormat) GetDefaultEditor()
    {
        // argsFormat must contain a single {0} placeholder for the file path (already quoted as needed)
        string? visual = Environment.GetEnvironmentVariable("VISUAL");
        string? editor = Environment.GetEnvironmentVariable("EDITOR");
        if (!string.IsNullOrWhiteSpace(visual)) return (visual, "{0}");
        if (!string.IsNullOrWhiteSpace(editor)) return (editor, "{0}");
        if (OperatingSystem.IsWindows()) return ("notepad", "{0}");
        return ("nano", "{0}"); // fall back to nano; user can close and set EDITOR if they prefer vi
    }

    private static string QuoteIfNeeded(string path)
        => path.Contains(' ') ? $"\"{path}\"" : path;

    private static string ReadLineWithInitial(string initial)
    {
        var buffer = new StringBuilder(initial);
        int cursor = buffer.Length; // insertion index in buffer
        int startLeft = Console.CursorLeft;
        int startTop = Console.CursorTop;
        int scrollStart = 0; // index in buffer where the viewport starts

        // If there is effectively no room on this line, move to a fresh line
        int initialAvail = Math.Max(0, Console.WindowWidth - startLeft - 1);
        if (initialAvail < 10)
        {
            Console.WriteLine();
            startLeft = 0;
            startTop = Console.CursorTop;
        }

        void Render()
        {
            int winWidth;
            try { winWidth = Console.WindowWidth; }
            catch { winWidth = 80; }

            int contentWidth = Math.Max(1, winWidth - startLeft - 1);

            // Keep cursor within viewport
            if (cursor < scrollStart) scrollStart = cursor;
            if (cursor - scrollStart >= contentWidth) scrollStart = Math.Max(0, cursor - contentWidth + 1);

            int end = Math.Min(buffer.Length, scrollStart + contentWidth);
            string view = buffer.ToString(scrollStart, end - scrollStart);

            // Show ellipsis if scrolled left/right
            if (scrollStart > 0 && view.Length > 0)
            {
                view = '…' + (view.Length > 1 ? view[1..] : string.Empty);
            }
            if (end < buffer.Length && view.Length > 0)
            {
                view = (view.Length > 1 ? view[..^1] : string.Empty) + '…';
            }

            // Render view padded to the full content width to clear remnants
            Console.SetCursorPosition(startLeft, startTop);
            Console.Write(view.PadRight(contentWidth));

            // Place cursor within the view
            int cursorCol = startLeft + Math.Min(cursor - scrollStart, contentWidth - 1);
            int safeCol = Math.Min(Math.Max(0, winWidth - 1), Math.Max(0, cursorCol));
            try { Console.SetCursorPosition(safeCol, startTop); } catch { }
        }

        // Initial render
        Render();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString();
            }
            else if (key.Key == ConsoleKey.Backspace)
            {
                if (cursor > 0)
                {
                    buffer.Remove(cursor - 1, 1);
                    cursor--;
                    Render();
                }
            }
            else if (key.Key == ConsoleKey.Delete)
            {
                if (cursor < buffer.Length)
                {
                    buffer.Remove(cursor, 1);
                    Render();
                }
            }
            else if (key.Key == ConsoleKey.LeftArrow)
            {
                if (cursor > 0) { cursor--; Render(); }
            }
            else if (key.Key == ConsoleKey.RightArrow)
            {
                if (cursor < buffer.Length) { cursor++; Render(); }
            }
            else if (key.Key == ConsoleKey.Home)
            {
                cursor = 0; Render();
            }
            else if (key.Key == ConsoleKey.End)
            {
                cursor = buffer.Length; Render();
            }
            else if (!char.IsControl(key.KeyChar))
            {
                buffer.Insert(cursor, key.KeyChar);
                cursor++;
                Render();
            }
        }
    }

    // PadColumn moved to Editor/EditorApp.UI.cs

    private bool HasPendingChanges(out int newCount, out int modCount, out int delCount)
    {
        newCount = _items.Count(i => i.State == ItemState.New);
        modCount = _items.Count(i => i.State == ItemState.Modified);
        delCount = _items.Count(i => i.State == ItemState.Deleted);
        return (newCount + modCount + delCount) > 0;
    }

    

    private static int CompareItems(Item a, Item b)
    {
        int c = string.Compare(a.ShortKey, b.ShortKey, StringComparison.Ordinal);
        if (c != 0) return c;
        return string.Compare(a.Label ?? string.Empty, b.Label ?? string.Empty, StringComparison.Ordinal);
    }

    // GetWindowWidth moved to Editor/EditorApp.UI.cs

    private void ConsolidateDuplicates()
    {
        var groups = _items.GroupBy(i => MakeKey(i.FullKey, i.Label)).ToList();
        foreach (var g in groups)
        {
            if (g.Count() <= 1) continue;
            var keep = g.FirstOrDefault(i => i.State != ItemState.Deleted) ?? g.First();
            foreach (var extra in g)
            {
                if (!ReferenceEquals(extra, keep))
                {
                    _items.Remove(extra);
                }
            }
        }
    }

    private sealed class TupleKeyComparer : IEqualityComparer<(string ShortKey, string? Label)>
    {
        public bool Equals((string ShortKey, string? Label) x, (string ShortKey, string? Label) y)
        {
            return string.Equals(x.ShortKey, y.ShortKey, StringComparison.Ordinal)
                && string.Equals(x.Label ?? string.Empty, y.Label ?? string.Empty, StringComparison.Ordinal);
        }

        public int GetHashCode((string ShortKey, string? Label) obj)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + obj.ShortKey.GetHashCode();
                h = h * 31 + (obj.Label ?? string.Empty).GetHashCode();
                return h;
            }
        }
    }

    // Layout moved to Core.UI.TableLayout

    private List<Item> GetVisibleItems()
    {
        // Delegate visibility to Core.ItemFilter to keep semantics centralized
        var mapper = new EditorMappers();
        var coreList = _items.Select(mapper.ToCoreItem).ToList();
        var indices = AppConfigCli.Core.ItemFilter.VisibleIndices(coreList, _label, _keyRegex);
        var result = new List<Item>(indices.Count);
        foreach (var idx in indices)
        {
            result.Add(_items[idx]);
        }
        return result;
    }

    // Helpers for building nested object/list structure from split keys
    private void AddPathToTree(Dictionary<string, object> node, string[] segments, string value)
    {
        if (segments.Length == 0)
        {
            node["__value"] = value;
            return;
        }
        var head = segments[0];
        if (!node.TryGetValue(head, out var child))
        {
            if (segments.Length == 1)
            {
                node[head] = value;
                return;
            }
            bool nextIsIndex = int.TryParse(segments[1], out _);
            if (nextIsIndex)
            {
                var list = new List<object?>();
                node[head] = list;
                AddPathToList(list, segments[1..], value);
            }
            else
            {
                var dict = new Dictionary<string, object>(StringComparer.Ordinal);
                node[head] = dict;
                AddPathToTree(dict, segments[1..], value);
            }
        }
        else if (child is string s)
        {
            bool nextIsIndex = segments.Length > 1 && int.TryParse(segments[1], out _);
            if (nextIsIndex)
            {
                var list = new List<object?>();
                node[head] = list;
                AddPathToList(list, segments[1..], value);
            }
            else
            {
                var dict = new Dictionary<string, object>(StringComparer.Ordinal) { ["__value"] = s };
                node[head] = dict;
                AddPathToTree(dict, segments[1..], value);
            }
        }
        else if (child is Dictionary<string, object> dict)
        {
            if (segments.Length == 1)
            {
                dict["__value"] = value;
            }
            else
            {
                AddPathToTree(dict, segments[1..], value);
            }
        }
        else if (child is List<object?> list)
        {
            AddPathToList(list, segments[1..], value);
        }
    }

    private void AddPathToList(List<object?> list, string[] segments, string value)
    {
        if (segments.Length == 0) return;
        var idxStr = segments[0];
        if (!int.TryParse(idxStr, out int idx))
        {
            EnsureListSize(list, 1);
            var head = 0;
            if (list[head] is not Dictionary<string, object> d)
            {
                d = new Dictionary<string, object>(StringComparer.Ordinal);
                list[head] = d;
            }
            AddPathToTree(d, segments, value);
            return;
        }
        EnsureListSize(list, idx + 1);
        var child = list[idx];
        if (segments.Length == 1)
        {
            list[idx] = value;
            return;
        }
        bool nextIsIndex = int.TryParse(segments[1], out _);
        if (child is null)
        {
            if (nextIsIndex)
            {
                var inner = new List<object?>();
                list[idx] = inner;
                AddPathToList(inner, segments[1..], value);
            }
            else
            {
                var d = new Dictionary<string, object>(StringComparer.Ordinal);
                list[idx] = d;
                AddPathToTree(d, segments[1..], value);
            }
        }
        else if (child is string s)
        {
            if (nextIsIndex)
            {
                var inner = new List<object?>();
                list[idx] = inner;
                AddPathToList(inner, segments[1..], value);
            }
            else
            {
                var d = new Dictionary<string, object>(StringComparer.Ordinal) { ["__value"] = s };
                list[idx] = d;
                AddPathToTree(d, segments[1..], value);
            }
        }
        else if (child is Dictionary<string, object> d2)
        {
            AddPathToTree(d2, segments[1..], value);
        }
        else if (child is List<object?> l)
        {
            AddPathToList(l, segments[1..], value);
        }
    }

    private static void EnsureListSize(List<object?> list, int size)
    {
        while (list.Count < size) list.Add(null);
    }

    private void SetKeyRegex(string[] args)
    {
        if (args.Length == 0)
        {
            _keyRegexPattern = null;
            _keyRegex = null;
            return;
        }
        var pattern = string.Join(' ', args);
        try
        {
            _keyRegex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            _keyRegexPattern = pattern;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Invalid regex: {ex.Message}");
            Console.WriteLine("Press Enter to continue...");
            Console.ReadLine();
        }
    }

    private List<int>? MapVisibleRangeToItemIndices(int start, int end, out string error)
    {
        // Use Core.ItemFilter to compute indices against a mapped Core list
        var mapper = new EditorMappers();
        var coreList = _items.Select(mapper.ToCoreItem).ToList();
        var indices = AppConfigCli.Core.ItemFilter.MapVisibleRangeToSourceIndices(coreList, _label, _keyRegex, start, end, out error);
        return indices;
    }
}
