namespace AppConfigCli.Core;

/// <summary>
/// Provides label mapping utilities to unify how label filters are expressed
/// for reads (selectors) and for writes/deletes against Azure App Configuration.
/// </summary>
public static class LabelFilter
{
    private const string EmptyLabelSelector = "\0"; // Special selector value used by Azure SDK for unlabeled entries

    /// <summary>
    /// Maps a user-provided label filter to the selector value expected by Azure App Configuration.
    /// null => null (any), "" => "\0" (unlabeled), other => literal.
    /// </summary>
    public static string? ForSelector(string? label)
    {
        if (label is null) return null;        // any label
        if (label.Length == 0) return EmptyLabelSelector; // unlabeled entries
        return label;                           // literal label
    }

    /// <summary>
    /// Maps a user-provided label to the value used for writes/deletes.
    /// null/"" => null (unlabeled), other => literal.
    /// </summary>
    public static string? ForWrite(string? label)
        => string.IsNullOrEmpty(label) ? null : label;
}

