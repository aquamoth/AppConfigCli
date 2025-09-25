using System;
using System.Collections.Generic;

namespace AppConfigCli;

internal static class HeaderLayout
{
    internal readonly record struct Segment(int Pos, string Text);

    // Computes positioned segments for the header lines (Prefix/Label/Filter)
    // Applying the same heuristics used previously in EditorApp.UI:
    // - Up to three items: Prefix (left), Label (center), Filter (right)
    // - If all three don't fit on one line, degrade to two lines
    // - If only Label is present (and Prefix hidden), center it
    // - Otherwise, left/right placement with a minimum gap
    public static List<List<Segment>> Compute(int width, string? prefix, string? label, string? filter, int gap = 2)
    {
        var lines = new List<List<Segment>>();
        string? p = string.IsNullOrWhiteSpace(prefix) ? null : prefix;
        string? l = string.IsNullOrWhiteSpace(label) ? null : label;
        string? f = string.IsNullOrWhiteSpace(filter) ? null : filter;

        bool Any(params string?[] xs)
        {
            foreach (var s in xs) if (!string.IsNullOrEmpty(s)) return true; return false;
        }

        List<Segment> Line(params Segment[] segs) => new List<Segment>(segs);

        bool FitsLeftRight(string left, string right)
            => left.Length + gap + right.Length <= width;

        bool TryAllThree(string pp, string ll, string ff, out List<Segment>? segs)
        {
            segs = null;
            int pLen = pp.Length, lLen = ll.Length, fLen = ff.Length;
            if (pLen + gap + fLen > width) return false;
            int rightStart = width - fLen;
            int centerStart = Math.Max(pLen + gap, (width - lLen) / 2);
            if (centerStart + lLen + gap > rightStart) return false;
            segs = Line(new Segment(0, pp), new Segment(centerStart, ll), new Segment(rightStart, ff));
            return true;
        }

        bool TryLeftRight(string left, string right, out List<Segment>? segs)
        {
            segs = null;
            if (!FitsLeftRight(left, right)) return false;
            int rightStart = width - right.Length;
            segs = Line(new Segment(0, left), new Segment(rightStart, right));
            return true;
        }

        if (!Any(p, l, f)) return lines;

        if (p is not null && l is not null && f is not null)
        {
            if (TryAllThree(p, l, f, out var one)) lines.Add(one!);
            else if (TryLeftRight(p, l, out var two)) { lines.Add(two!); lines.Add(Line(new Segment(0, f))); }
            else if (TryLeftRight(l, f, out var three)) { lines.Add(three!); lines.Add(Line(new Segment(0, p))); }
            else { lines.Add(Line(new Segment(0, p))); lines.Add(Line(new Segment(0, l))); lines.Add(Line(new Segment(0, f))); }
            return lines;
        }

        var present = new List<string>();
        if (p is not null) present.Add(p);
        if (l is not null) present.Add(l);
        if (f is not null) present.Add(f);

        if (present.Count == 1)
        {
            if (p is null && l is not null)
            {
                int centerStart = Math.Max(0, (width - l.Length) / 2);
                lines.Add(Line(new Segment(centerStart, l)));
            }
            else
            {
                lines.Add(Line(new Segment(0, present[0])));
            }
            return lines;
        }

        if (present.Count == 2)
        {
            // Special-case: prefix hidden, label+filter present -> try centered label + right filter
            if (p is null && l is not null && f is not null)
            {
                int fStart = width - f.Length;
                int lStart = Math.Max(0, (width - l.Length) / 2);
                if (lStart + l.Length + gap <= fStart)
                {
                    lines.Add(Line(new Segment(lStart, l), new Segment(fStart, f)));
                }
                else if (TryLeftRight(l, f, out var lf)) lines.Add(lf!);
                else if (TryLeftRight(f, l, out var fl)) lines.Add(fl!);
                else { lines.Add(Line(new Segment(0, l))); lines.Add(Line(new Segment(0, f))); }
            }
            else
            {
                var a = present[0];
                var b = present[1];
                if (TryLeftRight(a, b, out var ab)) lines.Add(ab!);
                else if (TryLeftRight(b, a, out var ba)) lines.Add(ba!);
                else { lines.Add(Line(new Segment(0, a))); lines.Add(Line(new Segment(0, b))); }
            }
            return lines;
        }

        return lines;
    }
}

