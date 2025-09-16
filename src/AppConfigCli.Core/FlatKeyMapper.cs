using System.Collections;

namespace AppConfigCli.Core;

/// <summary>
/// Maps between flat keys (split by a separator) and a nested tree supporting
/// objects (Dictionary), arrays (List), and direct node values via "__value".
/// </summary>
public static class FlatKeyMapper
{
    public const string NodeValueKey = "__value";

    public static Dictionary<string, object> BuildTree(IEnumerable<KeyValuePair<string, string>> flats, char separator)
    {
        var root = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var kv in flats)
        {
            var segments = kv.Key.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            AddPath(root, segments, kv.Value);
        }
        return root;
    }

    public static Dictionary<string, string> Flatten(object node, char separator)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var stack = new Stack<string>();

        void Walk(object? n)
        {
            switch (n)
            {
                case null:
                    // ignore
                    break;
                case string s:
                    var key = string.Join(separator, stack.Reverse());
                    result[key] = s;
                    break;
                case Dictionary<string, object> dict:
                    if (dict.TryGetValue(NodeValueKey, out var raw))
                    {
                        var key2 = string.Join(separator, stack.Reverse());
                        result[key2] = raw as string ?? string.Empty;
                    }
                    foreach (var kv in dict)
                    {
                        if (kv.Key == NodeValueKey) continue;
                        stack.Push(kv.Key);
                        Walk(kv.Value);
                        stack.Pop();
                    }
                    break;
                case List<object?> list:
                    for (int i = 0; i < list.Count; i++)
                    {
                        stack.Push(i.ToString());
                        Walk(list[i]);
                        stack.Pop();
                    }
                    break;
                default:
                    // Attempt to handle arbitrary IEnumerable as array-like, excluding string
                    if (n is IEnumerable enumerable && n is not string)
                    {
                        int i = 0;
                        foreach (var el in enumerable)
                        {
                            stack.Push(i.ToString());
                            Walk(el);
                            stack.Pop();
                            i++;
                        }
                    }
                    else
                    {
                        // Coerce other scalars to string
                        var k = string.Join(separator, stack.Reverse());
                        result[k] = n.ToString() ?? string.Empty;
                    }
                    break;
            }
        }

        Walk(node);
        return result;
    }

    private static void AddPath(Dictionary<string, object> node, string[] segments, string value)
    {
        if (segments.Length == 0)
        {
            node[NodeValueKey] = value;
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
                AddPath(dict, segments[1..], value);
            }
        }
        else if (child is string s)
        {
            bool nextIsIndex = segments.Length > 1 && int.TryParse(segments[1], out _);
            if (nextIsIndex)
            {
                var list = new List<object?>();
                node[head] = list;
                // keep current scalar as element 0 if no index given? not applicable here
                AddPathList(list, segments[1..], value);
            }
            else
            {
                var dict = new Dictionary<string, object>(StringComparer.Ordinal) { [NodeValueKey] = s };
                node[head] = dict;
                AddPath(dict, segments[1..], value);
            }
        }
        else if (child is Dictionary<string, object> dict)
        {
            if (segments.Length == 1)
            {
                dict[NodeValueKey] = value;
            }
            else
            {
                AddPath(dict, segments[1..], value);
            }
        }
        else if (child is List<object?> list)
        {
            AddPathList(list, segments[1..], value);
        }
    }

    private static void AddPathList(List<object?> list, string[] segments, string value)
    {
        if (segments.Length == 0) return;
        var idxStr = segments[0];
        if (!int.TryParse(idxStr, out int idx))
        {
            // Treat non-numeric under array as object at index 0
            EnsureListSize(list, 1);
            var head = 0;
            if (list[head] is not Dictionary<string, object> d)
            {
                d = new Dictionary<string, object>(StringComparer.Ordinal);
                list[head] = d;
            }
            AddPath(d, segments, value);
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
                AddPath(d, segments[1..], value);
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
                var d = new Dictionary<string, object>(StringComparer.Ordinal) { [NodeValueKey] = s };
                list[idx] = d;
                AddPath(d, segments[1..], value);
            }
        }
        else if (child is Dictionary<string, object> d2)
        {
            AddPath(d2, segments[1..], value);
        }
        else if (child is List<object?> l)
        {
            AddPathList(l, segments[1..], value);
        }
    }

    private static void EnsureListSize(List<object?> list, int size)
    {
        while (list.Count < size) list.Add(null);
    }
}

