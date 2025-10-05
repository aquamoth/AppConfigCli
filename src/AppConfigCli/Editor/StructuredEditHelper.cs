using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Encodings.Web;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using AppConfigCli.Core;

namespace AppConfigCli;

internal static class StructuredEditHelper
{
    public static string BuildJsonContent(IEnumerable<Item> visibleItems, string separator)
    {
        var flats = visibleItems
            .Where(i => i.State != ItemState.Deleted)
            .ToDictionary(i => i.ShortKey, i => i.Value ?? string.Empty, StringComparer.Ordinal);
        var root = FlatKeyMapper.BuildTree(flats, separator);
        // Use relaxed encoder so ASCII characters like '+' are not escaped (e.g., '\u002B').
        // This produces more natural JSON for editing in plain-text editors like Notepad.
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        return JsonSerializer.Serialize(root, jsonOptions);
    }

    public static (bool Ok, string Error, int Created, int Updated, int Deleted) ApplyJsonEdits(string json, string separator, List<Item> allItems, IEnumerable<Item> visibleUnderLabel, string? prefix, string? activeLabel)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (false, "Top-level JSON must be an object.", 0, 0, 0);
            }

            object Convert(JsonElement el)
            {
                switch (el.ValueKind)
                {
                    case JsonValueKind.Object:
                        var d = new Dictionary<string, object>(StringComparer.Ordinal);
                        foreach (var p in el.EnumerateObject()) d[p.Name] = Convert(p.Value);
                        return d;
                    case JsonValueKind.Array:
                        var list = new List<object?>();
                        foreach (var v in el.EnumerateArray()) list.Add(Convert(v));
                        return list;
                    case JsonValueKind.String: return el.GetString() ?? string.Empty;
                    case JsonValueKind.Number: return el.ToString();
                    case JsonValueKind.True: return "true";
                    case JsonValueKind.False: return "false";
                    case JsonValueKind.Null: return string.Empty;
                    default: return el.ToString();
                }
            }

            var rootObj = Convert(doc.RootElement);
            var flats = FlatKeyMapper.Flatten(rootObj, separator);
            var content = string.Join("\n", flats.Select(kv => kv.Key + "\t" + BulkEditHelper.EscapeValue(kv.Value)));
            var (c, u, d) = BulkEditHelper.ApplyEdits(content, allItems, visibleUnderLabel, prefix, activeLabel);
            return (true, string.Empty, c, u, d);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, 0, 0, 0);
        }
    }

    public static (bool Ok, string Error, int Created, int Updated, int Deleted) ApplyYamlEdits(string yaml, string separator, List<Item> allItems, IEnumerable<Item> visibleUnderLabel, string? prefix, string? activeLabel)
    {
        try
        {
            var deser = new DeserializerBuilder().WithNamingConvention(NullNamingConvention.Instance).Build();
            var raw = deser.Deserialize<object?>(yaml);
            if (raw is null)
            {
                return (false, "Top-level YAML must be an object or array.", 0, 0, 0);
            }

            object Normalize(object? n)
            {
                switch (n)
                {
                    case null:
                        return string.Empty;
                    case string s:
                        return s;
                    case IDictionary<object, object> dict:
                    {
                        var d = new Dictionary<string, object>(StringComparer.Ordinal);
                        foreach (var kv in dict)
                        {
                            var key = kv.Key?.ToString() ?? string.Empty;
                            d[key] = Normalize(kv.Value);
                        }
                        return d;
                    }
                    case IEnumerable<object?> list:
                    {
                        var l = new List<object?>();
                        foreach (var el in list)
                        {
                            l.Add(Normalize(el));
                        }
                        return l;
                    }
                    default:
                        // Also handle untyped IEnumerable (non-generic)
                        if (n is System.Collections.IEnumerable e && n is not string)
                        {
                            var l = new List<object?>();
                            foreach (var el in e)
                                l.Add(Normalize(el));
                            return l;
                        }
                        return n.ToString() ?? string.Empty;
                }
            }

            var normalized = Normalize(raw);
            if (normalized is not Dictionary<string, object> && normalized is not List<object?>)
            {
                return (false, "Top-level YAML must be an object or array.", 0, 0, 0);
            }

            var flats = FlatKeyMapper.Flatten(normalized, separator);
            var content = string.Join("\n", flats.Select(kv => kv.Key + "\t" + BulkEditHelper.EscapeValue(kv.Value)));
            var (c, u, d) = BulkEditHelper.ApplyEdits(content, allItems, visibleUnderLabel, prefix, activeLabel);
            return (true, string.Empty, c, u, d);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, 0, 0, 0);
        }
    }
}
