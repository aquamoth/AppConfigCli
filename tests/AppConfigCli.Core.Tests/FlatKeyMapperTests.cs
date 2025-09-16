using System.Collections.Generic;
using AppConfigCli.Core;
using FluentAssertions;
using Xunit;

public class FlatKeyMapperTests
{
    [Fact]
    public void BuildTree_creates_objects_arrays_and_values()
    {
        var flats = new Dictionary<string, string>
        {
            ["settings:color"] = "red",
            ["settings:title"] = "Hello",
            ["settings:array:0"] = "zero",
            ["settings:array:1:name"] = "first",
            ["settings:node"] = "val",
            ["settings:node:child"] = "sub"
        };

        var root = FlatKeyMapper.BuildTree(flats, ':');
        root.Should().ContainKey("settings");

        var settings = root["settings"].Should().BeOfType<Dictionary<string, object>>().Subject;
        settings["color"].Should().Be("red");
        settings["title"].Should().Be("Hello");

        // array
        var arr = settings["array"].Should().BeOfType<List<object?>>().Subject;
        arr.Should().HaveCountGreaterThanOrEqualTo(2);
        arr[0].Should().Be("zero");
        var idx1 = arr[1].Should().BeOfType<Dictionary<string, object>>().Subject;
        idx1["name"].Should().Be("first");

        // node with both value and child
        var node = settings["node"].Should().BeOfType<Dictionary<string, object>>().Subject;
        node[FlatKeyMapper.NodeValueKey].Should().Be("val");
        node["child"].Should().Be("sub");
    }

    [Fact]
    public void Flatten_roundtrips_back_to_flats()
    {
        var flats = new Dictionary<string, string>
        {
            ["a:b"] = "1",
            ["a:b:c"] = "2",
            ["a:list:0"] = "x",
            ["a:list:1:name"] = "n1",
        };

        var root = FlatKeyMapper.BuildTree(flats, ':');
        var roundtrip = FlatKeyMapper.Flatten(root, ':');

        roundtrip.Should().BeEquivalentTo(flats);
    }
}

