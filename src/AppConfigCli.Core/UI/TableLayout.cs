using System;
using System.Collections.Generic;
using System.Linq;

namespace AppConfigCli.Core.UI;

public static class TableLayout
{
    /// <summary>
    /// Computes column widths for Key, Label and Value given total width and items.
    /// Logic mirrors the CLI's layout rules: width-aware, hides/shrinks appropriately.
    /// </summary>
    public static void Compute(int totalWidth, bool includeValue, IReadOnlyList<Item> items,
        out int keyWidth, out int labelWidth, out int valueWidth)
    {
        const int minKey = 15;
        const int maxKey = 80;
        const int minLabel = 8;
        const int maxLabel = 25;
        const int minValue = 10;

        // Determine label width from data (clamped)
        var labelMax = Math.Max(6, items
            .Select(i => (string.IsNullOrEmpty(i.Label) ? "(none)" : i.Label!).Length)
            .DefaultIfEmpty(6).Max());
        labelWidth = Math.Clamp(labelMax, minLabel, maxLabel);

        // Fixed non-column characters in a row
        // Index column is right-aligned to 3 chars minimum, expands for 4+ digits
        int indexDigits = Math.Max(3, items.Count.ToString().Length);
        int fixedChars = includeValue ? (indexDigits + 9) : (indexDigits + 7); // indices + state + separators

        // Available space for key + label (+ value)
        int available = totalWidth - (fixedChars + labelWidth);

        int longestKey = items.Select(i => i.ShortKey.Length).DefaultIfEmpty(minKey).Max();

        if (includeValue)
        {
            // Ensure we have at least room for min key + min value; squeeze label when narrow
            if (available < minKey + minValue)
            {
                int deficit = (minKey + minValue) - available;
                labelWidth = Math.Max(minLabel, labelWidth - deficit);
                available = totalWidth - (fixedChars + labelWidth);
            }

            int maxKeyAllowed = Math.Min(maxKey, Math.Max(minKey, available - minValue));
            int neededKey = Math.Clamp(longestKey, minKey, maxKeyAllowed);
            keyWidth = neededKey;
            valueWidth = available - keyWidth;

            if (valueWidth < minValue)
            {
                int shortage = minValue - valueWidth;
                keyWidth = Math.Max(minKey, keyWidth - shortage);
                valueWidth = available - keyWidth;
            }

            keyWidth = Math.Max(minKey, keyWidth);
            valueWidth = Math.Max(1, valueWidth);
        }
        else
        {
            if (available < minKey)
            {
                int deficit = minKey - available;
                labelWidth = Math.Max(minLabel, labelWidth - deficit);
                available = totalWidth - (fixedChars + labelWidth);
            }

            keyWidth = Math.Clamp(longestKey, minKey, Math.Min(maxKey, available));
            valueWidth = 0;
        }
    }
}
