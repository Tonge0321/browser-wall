namespace BrowserWall.Core.Session;

/// <summary>
/// Description of one element in the UI Automation tree (filled in by the App while walking the tree).
/// <see cref="InsideDocument"/>: below an element of control type Document (web content);
/// <see cref="UnderToolBar"/>: has an ancestor of control type ToolBar.
/// </summary>
public sealed record AddressBarCandidate(bool IsEdit, bool HasValuePattern, bool InsideDocument, bool UnderToolBar, string? Name);

/// <summary>
/// Identifies the browser's address bar: an Edit that supports ValuePattern, is not inside web content, and is either under
/// a toolbar or named with a known address bar name. Recognized only when there is exactly one; zero or two or more means
/// not recognized (treated as unreadable, no guessing).
/// </summary>
public static class AddressBarPick
{
    /// <summary>
    /// Known address bar names: the Traditional Chinese Chrome name (measured) and the English Chrome name.
    /// These are match data, not UI text, so they are not in the string table.
    /// </summary>
    public static readonly IReadOnlyList<string> KnownNames = new[] { "網址與搜尋列", "Address and search bar" };

    public static bool IsCandidate(AddressBarCandidate c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return c.IsEdit
            && c.HasValuePattern
            && !c.InsideDocument
            && (c.UnderToolBar || (c.Name is not null && KnownNames.Contains(c.Name, StringComparer.Ordinal)));
    }

    /// <summary>Returns the index of the candidate when there is exactly one; otherwise -1.</summary>
    public static int Choose(IReadOnlyList<AddressBarCandidate> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        int found = -1;
        for (int i = 0; i < elements.Count; i++)
        {
            if (!IsCandidate(elements[i]))
            {
                continue;
            }

            if (found >= 0)
            {
                return -1;
            }

            found = i;
        }

        return found;
    }
}
