using Azure;
using Azure.Data.AppConfiguration;
using System.Text;
using Azure.Identity;
using Azure.Core;
using System.Text.Json;

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
        if (!string.IsNullOrWhiteSpace(connStr))
        {
            client = new ConfigurationClient(connStr);
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
            var credential = await BuildCredentialAsync(tenantId, options.Auth);

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
            whoAmIAction = async () => await WhoAmIAsync(credential, uri);
        }

        var repo = new AzureAppConfigRepository(client);
        // If no theme is specified, force the built-in 'default' preset
        var theme = ConsoleTheme.Load(options.Theme ?? "default", options.NoColor);
        var app = new EditorApp(repo, options.Prefix, options.Label, whoAmIAction, theme: theme);
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

    private static async Task<TokenCredential> BuildCredentialAsync(string? tenantId, string? authMode)
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
                var plan = await SelectAutoAuthPlanAsync(cli);
                return plan switch
                {
                    AutoAuthPlan.Cli => cli,
                    AutoAuthPlan.DeviceOnly => device,
                    _ => new ChainedTokenCredential(browser, device)
                };
        }
    }

    // Determines the auto-auth plan: prefer CLI if logged in; otherwise browser (non-WSL/headless) with device fallback; device-only on WSL/headless
    internal enum AutoAuthPlan { Cli, BrowserThenDevice, DeviceOnly }

    internal static async Task<AutoAuthPlan> SelectAutoAuthPlanAsync(AzureCliCredential cli)
    {
        // Try CLI silently first
        if (await IsCliLoggedInAsync(cli))
            return AutoAuthPlan.Cli;

        bool isWsl = IsWsl();
        bool headlessLinux = OperatingSystem.IsLinux() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));
        if (isWsl || headlessLinux)
            return AutoAuthPlan.DeviceOnly;

        return AutoAuthPlan.BrowserThenDevice;
    }

    internal static AutoAuthPlan SelectAutoAuthPlanForTests(bool cliLoggedIn, bool isWsl, bool hasDisplay)
    {
        if (cliLoggedIn) return AutoAuthPlan.Cli;
        bool headlessLinux = !OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && !hasDisplay; // tests provide hasDisplay
        if (isWsl || headlessLinux) return AutoAuthPlan.DeviceOnly;
        return AutoAuthPlan.BrowserThenDevice;
    }

    private static async Task<bool> IsCliLoggedInAsync(AzureCliCredential cli)
    {
        try
        {
            // Probe with azconfig scope; CLI does not prompt
            var ctx = new TokenRequestContext(new[] { "https://azconfig.io/.default" });
            await cli.GetTokenAsync(ctx, default);
            return true;
        }
        catch
        {
            return false;
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
        Console.WriteLine("Usage: AppConfigCli [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --prefix <value>              Optional. Key prefix (section) to load initially");
        Console.WriteLine("  --label <value>               Optional. Label filter");
        Console.WriteLine("  --endpoint <url>              Optional. App Configuration endpoint for AAD auth");
        Console.WriteLine("  --tenant <guid>               Optional. Entra ID tenant for AAD auth");
        Console.WriteLine("  --auth <mode>                 Optional. Auth method: auto|device|browser|cli|vscode (default: auto)");
        Console.WriteLine("  --theme <name>                Optional. Theme preset: default|mono|no-color|solarized");
        Console.WriteLine("  --no-color                    Optional. Disable color output (overrides theme)");
        Console.WriteLine("  --version                     Print version and exit");
        Console.WriteLine();
        Console.WriteLine("Environment:");
        Console.WriteLine("  APP_CONFIG_CONNECTION_STRING  Azure App Configuration connection string");
        Console.WriteLine("  APP_CONFIG_ENDPOINT           App Configuration endpoint (AAD auth)");
        Console.WriteLine("  AZURE_TENANT_ID               Entra ID tenant to authenticate against (AAD)");
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
