namespace BrowserWall.Core.State;

/// <summary>Result of the Z-order check.</summary>
public sealed record ZOrderReport(IReadOnlyList<long> ParkedAboveWall, bool ExpandedOrderBroken)
{
    public static ZOrderReport Ok { get; } = new(Array.Empty<long>(), false);

    public bool NeedsFix => ParkedAboveWall.Count > 0 || ExpandedOrderBroken;
}

/// <summary>
/// Checks the Z-order for parking and expanding: parked windows stay behind the wall; an expanded window sits
/// directly below the "back to grid" button and directly above the wall. Pure calculation; the App supplies the Z-order.
/// </summary>
public static class ZOrderCheck
{
    /// <param name="zOrder">All top-level windows, top to bottom.</param>
    /// <param name="wall">The wall.</param>
    /// <param name="parked">Parked managed windows (not including the expanded one).</param>
    /// <param name="expanded">The expanded managed window; null in grid mode.</param>
    /// <param name="button">The "back to grid" button; null in grid mode.</param>
    public static ZOrderReport Inspect(
        IReadOnlyList<long> zOrder,
        long wall,
        IReadOnlyCollection<long> parked,
        long? expanded,
        long? button)
    {
        ArgumentNullException.ThrowIfNull(zOrder);
        ArgumentNullException.ThrowIfNull(parked);

        var position = new Dictionary<long, int>(zOrder.Count);
        for (int i = 0; i < zOrder.Count; i++)
        {
            position.TryAdd(zOrder[i], i);
        }

        if (!position.TryGetValue(wall, out int wallIndex))
        {
            return ZOrderReport.Ok;
        }

        var above = new List<(int Index, long Id)>();
        foreach (long id in parked)
        {
            if (position.TryGetValue(id, out int index) && index < wallIndex)
            {
                above.Add((index, id));
            }
        }

        bool broken = false;
        if (expanded is long e)
        {
            if (!position.TryGetValue(e, out int expandedIndex))
            {
                broken = false; // The expanded window is gone (close detection handles it)
            }
            else
            {
                bool buttonOk = button is long b
                    && expandedIndex > 0
                    && zOrder[expandedIndex - 1] == b;
                bool wallOk = expandedIndex + 1 < zOrder.Count && zOrder[expandedIndex + 1] == wall;
                broken = !buttonOk || !wallOk;
            }
        }

        if (above.Count == 0 && !broken)
        {
            return ZOrderReport.Ok;
        }

        return new ZOrderReport(above.OrderBy(a => a.Index).Select(a => a.Id).ToList(), broken);
    }
}
