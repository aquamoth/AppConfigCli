using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AppConfigCli.Core;
using FluentAssertions;
using Xunit;

public class _Repository
{
    [Fact]
    public async Task in_memory_list_filters_by_prefix_and_label()
    {
        var seed = new[]
        {
            new ConfigEntry { Key = "p:Color", Label = "dev", Value = "red" },
            new ConfigEntry { Key = "p:Color", Label = "prod", Value = "blue" },
            new ConfigEntry { Key = "p:Title", Label = null, Value = "Hello" },
            new ConfigEntry { Key = "q:Other", Label = "dev", Value = "x" },
        };
        var repo = new InMemoryConfigRepository(seed);

        var any = await repo.ListAsync("p:", null);
        any.Should().HaveCount(3);

        var unlabeled = await repo.ListAsync("p:", "");
        unlabeled.Should().ContainSingle().And.OnlyContain(e => e.Label == null && e.Key.StartsWith("p:"));

        var dev = await repo.ListAsync("p:", "dev");
        dev.Should().ContainSingle().And.OnlyContain(e => e.Label == "dev" && e.Key.StartsWith("p:"));
    }

    [Fact]
    public async Task in_memory_upsert_and_delete_roundtrip()
    {
        var repo = new InMemoryConfigRepository();
        await repo.UpsertAsync(new ConfigEntry { Key = "p:Color", Label = "dev", Value = "red" });
        await repo.UpsertAsync(new ConfigEntry { Key = "p:Color", Label = "", Value = "blue" });

        var all = await repo.ListAsync("p:", null);
        all.Should().HaveCount(2);

        await repo.DeleteAsync("p:Color", "");
        var after = await repo.ListAsync("p:", null);
        after.Should().HaveCount(1).And.OnlyContain(e => e.Label == "dev");
    }
}
