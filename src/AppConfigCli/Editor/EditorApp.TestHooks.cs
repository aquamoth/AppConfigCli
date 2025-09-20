using System.Collections.Generic;
using System.Threading.Tasks;

namespace AppConfigCli;

// Test-only hooks for integration tests (internal visibility)
internal sealed partial class EditorApp
{
    internal List<Item> Test_Items => Items;
    internal Task Test_SaveAsync() => SaveAsync(pause: false);
}

