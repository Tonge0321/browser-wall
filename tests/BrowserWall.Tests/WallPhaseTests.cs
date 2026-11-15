using BrowserWall.Core.State;

namespace BrowserWall.Tests;

/// <summary>
/// The wall's phase: every operation in every phase and mode behaves exactly as before the phase refactor,
/// the boolean properties follow the phase, and the relations between phases hold across operation sequences.
/// The expected values below were captured from the code before the refactor and are written out cell by cell.
/// </summary>
public class WallPhaseTests
{
    private const WallPhase N = WallPhase.Normal;
    private const WallPhase AP = WallPhase.ActivationParkPending;
    private const WallPhase RP = WallPhase.RestorePending;
    private const WallPhase Min = WallPhase.Minimized;
    private const WallPhase Ex = WallPhase.Exiting;
    private const WallPhase ExMin = WallPhase.ExitingMinimized;

    private const WallMode Grid = WallMode.Grid;
    private const WallMode Expanded = WallMode.Expanded;
    private const WallMode Empty = WallMode.Empty;

    /// <summary>Start with cells: windows 1 and 2, a failed cell at index 2, and a pending cell close on window 2.</summary>
    private static WallState B()
    {
        var s = new WallState();
        s.AddWindow(1, "a");
        s.AddWindow(2, "b");
        s.AddFailed("f");
        s.CloseCell(2);
        return s;
    }

    /// <summary>Start with an empty wall.</summary>
    private static WallState E0() => new();

    /// <summary>Removes every cell; used in grid mode in the normal, activation-park-pending and restore-pending phases.</summary>
    private static WallState Clear(WallState s)
    {
        s.ReleaseFailed(2);
        s.WindowClosed(1);
        s.WindowClosed(2);
        return s;
    }

    private static WallState Do(WallState s, params Action<WallState>[] steps)
    {
        foreach (Action<WallState> step in steps)
        {
            step(s);
        }

        return s;
    }

    private static readonly Action<WallState> Press = s => s.CaptionPressStarted();
    private static readonly Action<WallState> Activate = s => s.WallActivated();
    private static readonly Action<WallState> Minimize = s => s.WallMinimized();
    private static readonly Action<WallState> Restore = s => s.WallRestored();
    private static readonly Action<WallState> Exit = s => s.BeginExit();
    private static readonly Action<WallState> Click0 = s => s.ClickCell(0);
    private static readonly Action<WallState> ClearAll = s => Clear(s);

    private sealed record Row(int No, WallPhase Phase, WallMode Mode, bool Caption, Func<WallState> Build);

    /// <summary>The 27 states (phase with or without a title bar button press × mode), each built from a start using public operations only.</summary>
    private static readonly Row[] Rows =
    {
        new(1, N, Grid, false, () => B()),
        new(2, N, Grid, true, () => Do(B(), Press)),
        new(3, AP, Grid, false, () => Do(B(), Activate)),
        new(4, AP, Grid, true, () => Do(B(), Activate, Press)),
        new(5, RP, Grid, false, () => Do(B(), Minimize, Restore)),
        new(6, RP, Grid, true, () => Do(B(), Minimize, Restore, Press)),
        new(7, Min, Grid, false, () => Do(B(), Minimize)),
        new(8, Ex, Grid, false, () => Do(B(), Exit)),
        new(9, ExMin, Grid, false, () => Do(B(), Minimize, Exit)),
        new(10, N, Expanded, false, () => Do(B(), Click0)),
        new(11, N, Expanded, true, () => Do(B(), Click0, Press)),
        new(12, AP, Expanded, false, () => Do(B(), Activate, Click0)),
        new(13, AP, Expanded, true, () => Do(B(), Activate, Click0, Press)),
        new(14, RP, Expanded, false, () => Do(B(), Click0, Minimize, Restore)),
        new(15, RP, Expanded, true, () => Do(B(), Click0, Minimize, Restore, Press)),
        new(16, Min, Expanded, false, () => Do(B(), Click0, Minimize)),
        new(17, Ex, Expanded, false, () => Do(B(), Click0, Exit)),
        new(18, ExMin, Expanded, false, () => Do(B(), Click0, Minimize, Exit)),
        new(19, N, Empty, false, () => Do(B(), ClearAll)),
        new(20, N, Empty, true, () => Do(B(), Press, ClearAll)),
        new(21, AP, Empty, false, () => Do(B(), Activate, ClearAll)),
        new(22, AP, Empty, true, () => Do(B(), Activate, Press, ClearAll)),
        new(23, RP, Empty, false, () => Do(B(), Minimize, Restore, ClearAll)),
        new(24, RP, Empty, true, () => Do(B(), Minimize, Restore, Press, ClearAll)),
        new(25, Min, Empty, false, () => Do(B(), s => s.ReleaseFailed(2), Minimize, s => s.WindowClosed(1), s => s.WindowClosed(2))),
        new(26, Ex, Empty, false, () => Do(E0(), Exit)),
        new(27, ExMin, Empty, false, () => Do(E0(), Minimize, Exit)),
    };

    private sealed record Op(string Name, Func<WallState, string> Run);

    private static string A(IReadOnlyList<WallAction> actions) =>
        actions.Count == 0 ? "-" : string.Join(",", actions.Select(a => a.WindowId is long id ? $"{a.Kind}({id})" : a.Kind.ToString()));

    private static string Ids(IEnumerable<long> ids) => "[" + string.Join(",", ids) + "]";

    /// <summary>Every public operation (two arguments each for the title bar press end and activation).</summary>
    private static readonly Op[] Ops =
    {
        new("CloseCell(1)", s => A(s.CloseCell(1))),
        new("CloseStillOpen(2)", s => A(s.CloseStillOpen(2))),
        new("ExpandTimedOutClose", s => A(s.ExpandTimedOutClose())),
        new("ExpandNewWindow(2)", s => A(s.ExpandNewWindow(2))),
        new("SetWindowUrl(1)", s => s.SetWindowUrl(1, "n") ? "true" : "false"),
        new("BeginExit", s => A(s.BeginExit())),
        new("FinishExit([1])", s => A(s.FinishExit(new long[] { 1 }))),
        new("AddWindow(9)", s => A(s.AddWindow(9, "g"))),
        new("AdoptWindow(9)", s => A(s.AdoptWindow(9))),
        new("AutoAddWindow(9)", s => A(s.AutoAddWindow(9))),
        new("SwapCells(0,1)", s => A(s.SwapCells(0, 1))),
        new("ReleaseWindow(2)", s => A(s.ReleaseWindow(2))),
        new("WallMinimized", s => A(s.WallMinimized())),
        new("WallRestored", s => A(s.WallRestored())),
        new("SettleRestore", s => A(s.SettleRestore())),
        new("DeferredParkTargets([1,2])", s => Ids(s.DeferredParkTargets(new long[] { 1, 2 }))),
        new("CaptionPressStarted", s =>
        {
            s.CaptionPressStarted();
            return "void";
        }),
        new("CaptionPressEnded(true)", s => A(s.CaptionPressEnded(true))),
        new("CaptionPressEnded(false)", s => A(s.CaptionPressEnded(false))),
        new("AddFailed", s => A(s.AddFailed("h"))),
        new("ReleaseFailed(2)", s => A(s.ReleaseFailed(2))),
        new("WallActivated()", s => A(s.WallActivated())),
        new("WallActivated(true)", s => A(s.WallActivated(true))),
        new("ClickCell(0)", s => A(s.ClickCell(0))),
        new("Shrink", s => A(s.Shrink())),
        new("WindowClosed(1)", s => A(s.WindowClosed(1))),
        new("SyncAlive([2])", s => A(s.SyncAlive(new long[] { 2 }))),
        new("WindowMinimized(1)", s => A(s.WindowMinimized(1))),
        new("WallResized", s => A(s.WallResized())),
    };

    private static string Run(Op op, WallState s)
    {
        try
        {
            return op.Run(s);
        }
        catch (Exception e)
        {
            return "!" + e.GetType().Name;
        }
    }

    private static char Bit(bool b) => b ? '1' : '0';

    /// <summary>Every public property in one line.</summary>
    private static string Snapshot(WallState s)
    {
        string slots = string.Join(",", s.Slots.Select(x => x.Kind == WallSlotKind.Window ? $"W{x.WindowId}{x.Url}" : $"F{x.Url}"));
        return $"{s.Phase}|{s.Mode}|e{(s.ExpandedWindowId is long e ? e.ToString() : "-")}"
            + $"|MXRAC{Bit(s.IsWallMinimized)}{Bit(s.IsExiting)}{Bit(s.RestorePending)}{Bit(s.ActivationParkPending)}{Bit(s.CaptionPressPending)}"
            + $"|auto{Bit(s.CanAutoAdd)}|win{Bit(s.HasManagedWindows)}|free{Bit(s.HasFreeSlot)}/{s.FreeSlotCount}"
            + $"|slots[{slots}]|ids{Ids(s.WindowIds)}|parked{Ids(s.ParkedWindowIds)}|q{Ids(s.CloseExpandQueue)}";
    }

    private static void AssertAtRow(Row row, WallState s)
    {
        Assert.True(row.Phase == s.Phase && row.Mode == s.Mode && row.Caption == s.CaptionPressPending,
            $"Row {row.No} built {s.Phase}/{s.Mode}/caption={s.CaptionPressPending}, expected {row.Phase}/{row.Mode}/caption={row.Caption}");
    }

    private static string CellLine(Row row, Op op)
    {
        WallState s = row.Build();
        AssertAtRow(row, s);
        string result = Run(op, s);
        return $"{row.No:D2} {op.Name} => {result} # {Snapshot(s)}";
    }

    [Fact(DisplayName = "Phase_OperationTableMatchesVersion9")]
    public void Phase_OperationTableMatchesVersion9()
    {
        string[] expected = ExpectedTable.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();
        var actual = new List<string>();
        foreach (Row row in Rows)
        {
            foreach (Op op in Ops)
            {
                actual.Add(CellLine(row, op));
            }
        }

        Assert.Equal(Rows.Length * Ops.Length, expected.Length);
        var mismatches = new List<string>();
        for (int i = 0; i < actual.Count; i++)
        {
            if (actual[i] != expected[i])
            {
                mismatches.Add($"expected: {expected[i]}\n  actual: {actual[i]}");
            }
        }

        Assert.True(mismatches.Count == 0, $"{mismatches.Count} cells differ:\n" + string.Join("\n", mismatches.Take(20)));
    }

    [Fact(DisplayName = "Phase_PropertiesDerivedFromPhase")]
    public void Phase_PropertiesDerivedFromPhase()
    {
        // Phase, then IsWallMinimized, IsExiting, RestorePending, ActivationParkPending, CaptionPressPending, CanAutoAdd.
        var expected = new (int Row, WallPhase Phase, bool M, bool X, bool R, bool A, bool C, bool Auto)[]
        {
            (1, N, false, false, false, false, false, true),
            (2, N, false, false, false, false, true, true),
            (3, AP, false, false, false, true, false, true),
            (4, AP, false, false, false, true, true, true),
            (5, RP, false, false, true, false, false, false),
            (6, RP, false, false, true, false, true, false),
            (7, Min, true, false, false, false, false, false),
            (8, Ex, false, true, false, false, false, false),
            (9, ExMin, true, true, false, false, false, false),
        };

        foreach (var e in expected)
        {
            WallState s = Rows[e.Row - 1].Build();
            Assert.Equal(e.Phase, s.Phase);
            Assert.Equal(Grid, s.Mode);
            Assert.Equal(e.M, s.IsWallMinimized);
            Assert.Equal(e.X, s.IsExiting);
            Assert.Equal(e.R, s.RestorePending);
            Assert.Equal(e.A, s.ActivationParkPending);
            Assert.Equal(e.C, s.CaptionPressPending);
            Assert.Equal(e.Auto, s.CanAutoAdd);
        }
    }

    /// <summary>Operations that change the phase or the mode; used for the sequence walk.</summary>
    private static readonly Op[] Alphabet =
    {
        new("WallActivated()", s => A(s.WallActivated())),
        new("WallActivated(true)", s => A(s.WallActivated(true))),
        new("CaptionPressEnded(true)", s => A(s.CaptionPressEnded(true))),
        new("CaptionPressEnded(false)", s => A(s.CaptionPressEnded(false))),
        new("WallMinimized", s => A(s.WallMinimized())),
        new("WallRestored", s => A(s.WallRestored())),
        new("SettleRestore", s => A(s.SettleRestore())),
        new("DeferredParkTargets([1,2])", s => Ids(s.DeferredParkTargets(new long[] { 1, 2 }))),
        new("WallResized", s => A(s.WallResized())),
        new("ClickCell(0)", s => A(s.ClickCell(0))),
        new("Shrink", s => A(s.Shrink())),
        new("WindowClosed(1)", s => A(s.WindowClosed(1))),
        new("WindowClosed(2)", s => A(s.WindowClosed(2))),
        new("BeginExit", s => A(s.BeginExit())),
        new("FinishExit([1])", s => A(s.FinishExit(new long[] { 1 }))),
    };

    [Fact(DisplayName = "Phase_RelationsHoldAcrossOperationSequences")]
    public void Phase_RelationsHoldAcrossOperationSequences()
    {
        int checkedSteps = 0;

        // From the start with cells: every sequence of length 1 to 4.
        Walk(() => B(), new List<int>(), 4, "B", ref checkedSteps);

        // From each of the 27 states: every continuation of length 1 to 3.
        foreach (Row row in Rows)
        {
            Walk(() =>
            {
                WallState s = row.Build();
                AssertAtRow(row, s);
                return s;
            }, new List<int>(), 3, $"row {row.No}", ref checkedSteps);
        }

        Assert.Equal(54240 + (27 * 3615), checkedSteps);
    }

    /// <summary>Depth-first: each sequence is replayed from the start, and only its last step is checked (its prefixes were checked as shorter sequences).</summary>
    private static void Walk(Func<WallState> start, List<int> prefix, int maxLength, string from, ref int checkedSteps)
    {
        if (prefix.Count == maxLength)
        {
            return;
        }

        for (int i = 0; i < Alphabet.Length; i++)
        {
            WallState s = start();
            foreach (int p in prefix)
            {
                Alphabet[p].Run(s);
            }

            string where = $"{from}: {string.Join(" > ", prefix.Append(i).Select(k => Alphabet[k].Name))}";
            CheckRelations(s, where + " (before)");
            bool wasExiting = s.IsExiting;
            bool wasMinimized = s.IsWallMinimized;
            string before = Snapshot(s);
            Op op = Alphabet[i];
            op.Run(s);
            CheckRelations(s, where);

            if (op.Name == "BeginExit")
            {
                Assert.True(wasMinimized == s.IsWallMinimized, $"{where}: closing changed the minimized value");
            }

            // Once closing, nothing but the final exit step changes any public property.
            // (Handling a cell close that is still open after 2 seconds is the one exception; it is not in this alphabet.)
            if (wasExiting && op.Name != "FinishExit([1])")
            {
                Assert.True(before == Snapshot(s), $"{where}: changed state while closing");
            }

            checkedSteps++;
            prefix.Add(i);
            Walk(start, prefix, maxLength, from, ref checkedSteps);
            prefix.RemoveAt(prefix.Count - 1);
        }
    }

    private static void CheckRelations(WallState s, string where)
    {
        int pending = (s.IsWallMinimized ? 1 : 0) + (s.RestorePending ? 1 : 0) + (s.ActivationParkPending ? 1 : 0);
        Assert.True(pending <= 1, $"{where}: minimized, restore pending and activation park pending overlap ({Snapshot(s)})");
        Assert.True(!(s.IsWallMinimized && s.CaptionPressPending), $"{where}: caption press while minimized ({Snapshot(s)})");
        Assert.True(!s.IsExiting || (!s.RestorePending && !s.ActivationParkPending && !s.CaptionPressPending),
            $"{where}: pending marks while closing ({Snapshot(s)})");
    }

    [Fact(DisplayName = "Phase_RandomSequencesMatchVersion9")]
    public void Phase_RandomSequencesMatchVersion9()
    {
        Assert.Equal(ExpectedRandomHash, RandomSequencesHash());
    }

    private static ulong RandomSequencesHash()
    {
        var random = new Random(20261115);
        ulong hash = 14695981039346656037UL;
        for (int run = 0; run < 2000; run++)
        {
            WallState s = run % 2 == 0 ? B() : E0();
            for (int step = 0; step < 40; step++)
            {
                Op op = Ops[random.Next(Ops.Length)];
                string line = $"{run}.{step} {op.Name} => {Run(op, s)} # {Snapshot(s)}\n";
                foreach (char c in line)
                {
                    hash ^= c;
                    hash *= 1099511628211UL;
                }
            }
        }

        return hash;
    }

    // ---- Expected values captured from the code before the phase refactor: one line per state row and operation,
    // "row operation => result # every public property afterwards". ----
    private const ulong ExpectedRandomHash = 0x84FC0F020E32CCF1UL;

    private const string ExpectedTable = """
        01 CloseCell(1) => CloseWindow(1) # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        01 CloseStillOpen(2) => Expand(2),ShowShrinkButton # Normal|Expanded|e2|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1]|q[]
        01 ExpandTimedOutClose => - # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        01 ExpandNewWindow(2) => Expand(2),ShowShrinkButton # Normal|Expanded|e2|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1]|q[]
        01 SetWindowUrl(1) => true # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/13|slots[W1n,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        01 BeginExit => CloseWindow(1),CloseWindow(2) # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        01 FinishExit([1]) => - # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        01 AddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/12|slots[W1a,W2b,Ff,W9g]|ids[1,2,9]|parked[1,2,9]|q[]
        01 AdoptWindow(9) => Park(9),HideTaskbarButton(9),Relayout # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/12|slots[W1a,W2b,Ff,W9]|ids[1,2,9]|parked[1,2,9]|q[]
        01 AutoAddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/12|slots[W1a,W2b,Ff,W9]|ids[1,2,9]|parked[1,2,9]|q[]
        01 SwapCells(0,1) => Relayout # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/13|slots[W2b,W1a,Ff]|ids[2,1]|parked[2,1]|q[]
        01 ReleaseWindow(2) => Release(2),Relayout # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/14|slots[W1a,Ff]|ids[1]|parked[1]|q[]
        01 WallMinimized => Minimize(1),Minimize(2) # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        01 WallRestored => - # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        01 SettleRestore => - # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        01 DeferredParkTargets([1,2]) => [1,2] # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        01 CaptionPressStarted => void # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        01 CaptionPressEnded(true) => - # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        01 CaptionPressEnded(false) => - # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        01 AddFailed => Relayout # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/12|slots[W1a,W2b,Ff,Fh]|ids[1,2]|parked[1,2]|q[]
        01 ReleaseFailed(2) => Relayout # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/14|slots[W1a,W2b]|ids[1,2]|parked[1,2]|q[]
        01 WallActivated() => ParkDeferred(1),ParkDeferred(2) # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        01 WallActivated(true) => - # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        01 ClickCell(0) => Expand(1),ShowShrinkButton # Normal|Expanded|e1|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        01 Shrink => - # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        01 WindowClosed(1) => Relayout # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/14|slots[W2b,Ff]|ids[2]|parked[2]|q[]
        01 SyncAlive([2]) => Relayout # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/14|slots[W2b,Ff]|ids[2]|parked[2]|q[]
        01 WindowMinimized(1) => Restore(1),Park(1) # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        01 WallResized => Park(1),Park(2),Relayout # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        02 CloseCell(1) => CloseWindow(1) # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        02 CloseStillOpen(2) => - # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[2]
        02 ExpandTimedOutClose => - # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        02 ExpandNewWindow(2) => - # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        02 SetWindowUrl(1) => true # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/13|slots[W1n,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        02 BeginExit => CloseWindow(1),CloseWindow(2) # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        02 FinishExit([1]) => - # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        02 AddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/12|slots[W1a,W2b,Ff,W9g]|ids[1,2,9]|parked[1,2,9]|q[]
        02 AdoptWindow(9) => Park(9),HideTaskbarButton(9),Relayout # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/12|slots[W1a,W2b,Ff,W9]|ids[1,2,9]|parked[1,2,9]|q[]
        02 AutoAddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/12|slots[W1a,W2b,Ff,W9]|ids[1,2,9]|parked[1,2,9]|q[]
        02 SwapCells(0,1) => Relayout # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/13|slots[W2b,W1a,Ff]|ids[2,1]|parked[2,1]|q[]
        02 ReleaseWindow(2) => Release(2),Relayout # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/14|slots[W1a,Ff]|ids[1]|parked[1]|q[]
        02 WallMinimized => Minimize(1),Minimize(2) # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        02 WallRestored => - # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        02 SettleRestore => - # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        02 DeferredParkTargets([1,2]) => [1,2] # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        02 CaptionPressStarted => void # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        02 CaptionPressEnded(true) => ParkDeferred(1),ParkDeferred(2) # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        02 CaptionPressEnded(false) => - # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        02 AddFailed => Relayout # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/12|slots[W1a,W2b,Ff,Fh]|ids[1,2]|parked[1,2]|q[]
        02 ReleaseFailed(2) => Relayout # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/14|slots[W1a,W2b]|ids[1,2]|parked[1,2]|q[]
        02 WallActivated() => ParkDeferred(1),ParkDeferred(2) # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        02 WallActivated(true) => - # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        02 ClickCell(0) => Expand(1),ShowShrinkButton # Normal|Expanded|e1|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        02 Shrink => - # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        02 WindowClosed(1) => Relayout # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/14|slots[W2b,Ff]|ids[2]|parked[2]|q[]
        02 SyncAlive([2]) => Relayout # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/14|slots[W2b,Ff]|ids[2]|parked[2]|q[]
        02 WindowMinimized(1) => Restore(1),Park(1) # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        02 WallResized => ParkDeferred(1),ParkDeferred(2),Relayout # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        03 CloseCell(1) => CloseWindow(1) # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        03 CloseStillOpen(2) => Expand(2),ShowShrinkButton # ActivationParkPending|Expanded|e2|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1]|q[]
        03 ExpandTimedOutClose => - # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        03 ExpandNewWindow(2) => Expand(2),ShowShrinkButton # ActivationParkPending|Expanded|e2|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1]|q[]
        03 SetWindowUrl(1) => true # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/13|slots[W1n,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        03 BeginExit => CloseWindow(1),CloseWindow(2) # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        03 FinishExit([1]) => - # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        03 AddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/12|slots[W1a,W2b,Ff,W9g]|ids[1,2,9]|parked[1,2,9]|q[]
        03 AdoptWindow(9) => Park(9),HideTaskbarButton(9),Relayout # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/12|slots[W1a,W2b,Ff,W9]|ids[1,2,9]|parked[1,2,9]|q[]
        03 AutoAddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/12|slots[W1a,W2b,Ff,W9]|ids[1,2,9]|parked[1,2,9]|q[]
        03 SwapCells(0,1) => Relayout # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/13|slots[W2b,W1a,Ff]|ids[2,1]|parked[2,1]|q[]
        03 ReleaseWindow(2) => Release(2),Relayout # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/14|slots[W1a,Ff]|ids[1]|parked[1]|q[]
        03 WallMinimized => Minimize(1),Minimize(2) # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        03 WallRestored => - # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        03 SettleRestore => - # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        03 DeferredParkTargets([1,2]) => [1,2] # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        03 CaptionPressStarted => void # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        03 CaptionPressEnded(true) => - # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        03 CaptionPressEnded(false) => - # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        03 AddFailed => Relayout # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/12|slots[W1a,W2b,Ff,Fh]|ids[1,2]|parked[1,2]|q[]
        03 ReleaseFailed(2) => Relayout # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/14|slots[W1a,W2b]|ids[1,2]|parked[1,2]|q[]
        03 WallActivated() => ParkDeferred(1),ParkDeferred(2) # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        03 WallActivated(true) => - # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        03 ClickCell(0) => Expand(1),ShowShrinkButton # ActivationParkPending|Expanded|e1|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        03 Shrink => - # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        03 WindowClosed(1) => Relayout # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/14|slots[W2b,Ff]|ids[2]|parked[2]|q[]
        03 SyncAlive([2]) => Relayout # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/14|slots[W2b,Ff]|ids[2]|parked[2]|q[]
        03 WindowMinimized(1) => Restore(1),Park(1) # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        03 WallResized => ParkDeferred(1),ParkDeferred(2),Relayout # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        04 CloseCell(1) => CloseWindow(1) # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        04 CloseStillOpen(2) => - # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[2]
        04 ExpandTimedOutClose => - # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        04 ExpandNewWindow(2) => - # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        04 SetWindowUrl(1) => true # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/13|slots[W1n,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        04 BeginExit => CloseWindow(1),CloseWindow(2) # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        04 FinishExit([1]) => - # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        04 AddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/12|slots[W1a,W2b,Ff,W9g]|ids[1,2,9]|parked[1,2,9]|q[]
        04 AdoptWindow(9) => Park(9),HideTaskbarButton(9),Relayout # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/12|slots[W1a,W2b,Ff,W9]|ids[1,2,9]|parked[1,2,9]|q[]
        04 AutoAddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/12|slots[W1a,W2b,Ff,W9]|ids[1,2,9]|parked[1,2,9]|q[]
        04 SwapCells(0,1) => Relayout # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/13|slots[W2b,W1a,Ff]|ids[2,1]|parked[2,1]|q[]
        04 ReleaseWindow(2) => Release(2),Relayout # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/14|slots[W1a,Ff]|ids[1]|parked[1]|q[]
        04 WallMinimized => Minimize(1),Minimize(2) # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        04 WallRestored => - # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        04 SettleRestore => - # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        04 DeferredParkTargets([1,2]) => [1,2] # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        04 CaptionPressStarted => void # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        04 CaptionPressEnded(true) => ParkDeferred(1),ParkDeferred(2) # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        04 CaptionPressEnded(false) => - # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        04 AddFailed => Relayout # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/12|slots[W1a,W2b,Ff,Fh]|ids[1,2]|parked[1,2]|q[]
        04 ReleaseFailed(2) => Relayout # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/14|slots[W1a,W2b]|ids[1,2]|parked[1,2]|q[]
        04 WallActivated() => ParkDeferred(1),ParkDeferred(2) # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        04 WallActivated(true) => - # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        04 ClickCell(0) => Expand(1),ShowShrinkButton # ActivationParkPending|Expanded|e1|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        04 Shrink => - # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        04 WindowClosed(1) => Relayout # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/14|slots[W2b,Ff]|ids[2]|parked[2]|q[]
        04 SyncAlive([2]) => Relayout # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/14|slots[W2b,Ff]|ids[2]|parked[2]|q[]
        04 WindowMinimized(1) => Restore(1),Park(1) # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        04 WallResized => ParkDeferred(1),ParkDeferred(2),Relayout # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        05 CloseCell(1) => - # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        05 CloseStillOpen(2) => - # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[2]
        05 ExpandTimedOutClose => - # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        05 ExpandNewWindow(2) => - # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        05 SetWindowUrl(1) => true # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/13|slots[W1n,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        05 BeginExit => CloseWindow(1),CloseWindow(2) # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        05 FinishExit([1]) => - # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        05 AddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/12|slots[W1a,W2b,Ff,W9g]|ids[1,2,9]|parked[1,2,9]|q[]
        05 AdoptWindow(9) => Park(9),HideTaskbarButton(9),Relayout # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/12|slots[W1a,W2b,Ff,W9]|ids[1,2,9]|parked[1,2,9]|q[]
        05 AutoAddWindow(9) => - # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        05 SwapCells(0,1) => - # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        05 ReleaseWindow(2) => Release(2),Relayout # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/14|slots[W1a,Ff]|ids[1]|parked[1]|q[]
        05 WallMinimized => Minimize(1),Minimize(2) # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        05 WallRestored => - # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        05 SettleRestore => Restore(1),Park(1),Restore(2),Park(2),Relayout # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        05 DeferredParkTargets([1,2]) => [1,2] # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        05 CaptionPressStarted => void # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        05 CaptionPressEnded(true) => - # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        05 CaptionPressEnded(false) => - # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        05 AddFailed => Relayout # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/12|slots[W1a,W2b,Ff,Fh]|ids[1,2]|parked[1,2]|q[]
        05 ReleaseFailed(2) => Relayout # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/14|slots[W1a,W2b]|ids[1,2]|parked[1,2]|q[]
        05 WallActivated() => - # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        05 WallActivated(true) => - # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        05 ClickCell(0) => - # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        05 Shrink => - # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        05 WindowClosed(1) => Relayout # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/14|slots[W2b,Ff]|ids[2]|parked[2]|q[]
        05 SyncAlive([2]) => Relayout # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/14|slots[W2b,Ff]|ids[2]|parked[2]|q[]
        05 WindowMinimized(1) => - # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        05 WallResized => Relayout # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        06 CloseCell(1) => - # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        06 CloseStillOpen(2) => - # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[2]
        06 ExpandTimedOutClose => - # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        06 ExpandNewWindow(2) => - # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        06 SetWindowUrl(1) => true # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/13|slots[W1n,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        06 BeginExit => CloseWindow(1),CloseWindow(2) # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        06 FinishExit([1]) => - # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        06 AddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/12|slots[W1a,W2b,Ff,W9g]|ids[1,2,9]|parked[1,2,9]|q[]
        06 AdoptWindow(9) => Park(9),HideTaskbarButton(9),Relayout # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/12|slots[W1a,W2b,Ff,W9]|ids[1,2,9]|parked[1,2,9]|q[]
        06 AutoAddWindow(9) => - # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        06 SwapCells(0,1) => - # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        06 ReleaseWindow(2) => Release(2),Relayout # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/14|slots[W1a,Ff]|ids[1]|parked[1]|q[]
        06 WallMinimized => Minimize(1),Minimize(2) # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        06 WallRestored => - # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        06 SettleRestore => - # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        06 DeferredParkTargets([1,2]) => [1,2] # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        06 CaptionPressStarted => void # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        06 CaptionPressEnded(true) => HideShrinkButton # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        06 CaptionPressEnded(false) => - # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        06 AddFailed => Relayout # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/12|slots[W1a,W2b,Ff,Fh]|ids[1,2]|parked[1,2]|q[]
        06 ReleaseFailed(2) => Relayout # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/14|slots[W1a,W2b]|ids[1,2]|parked[1,2]|q[]
        06 WallActivated() => - # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        06 WallActivated(true) => - # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        06 ClickCell(0) => - # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        06 Shrink => - # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        06 WindowClosed(1) => Relayout # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/14|slots[W2b,Ff]|ids[2]|parked[2]|q[]
        06 SyncAlive([2]) => Relayout # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/14|slots[W2b,Ff]|ids[2]|parked[2]|q[]
        06 WindowMinimized(1) => - # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        06 WallResized => Relayout # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        07 CloseCell(1) => - # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        07 CloseStillOpen(2) => - # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[2]
        07 ExpandTimedOutClose => - # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        07 ExpandNewWindow(2) => - # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        07 SetWindowUrl(1) => true # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1n,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        07 BeginExit => CloseWindow(1),CloseWindow(2) # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        07 FinishExit([1]) => - # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        07 AddWindow(9) => Minimize(9),HideTaskbarButton(9),Relayout # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/12|slots[W1a,W2b,Ff,W9g]|ids[1,2,9]|parked[1,2,9]|q[]
        07 AdoptWindow(9) => - # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        07 AutoAddWindow(9) => - # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        07 SwapCells(0,1) => - # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        07 ReleaseWindow(2) => - # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        07 WallMinimized => - # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        07 WallRestored => Restore(1),Park(1),Restore(2),Park(2),Relayout # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        07 SettleRestore => - # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        07 DeferredParkTargets([1,2]) => [] # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        07 CaptionPressStarted => void # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        07 CaptionPressEnded(true) => - # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        07 CaptionPressEnded(false) => - # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        07 AddFailed => Relayout # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/12|slots[W1a,W2b,Ff,Fh]|ids[1,2]|parked[1,2]|q[]
        07 ReleaseFailed(2) => - # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        07 WallActivated() => - # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        07 WallActivated(true) => - # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        07 ClickCell(0) => - # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        07 Shrink => - # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        07 WindowClosed(1) => Relayout # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/14|slots[W2b,Ff]|ids[2]|parked[2]|q[]
        07 SyncAlive([2]) => Relayout # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/14|slots[W2b,Ff]|ids[2]|parked[2]|q[]
        07 WindowMinimized(1) => - # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        07 WallResized => - # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        08 CloseCell(1) => - # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        08 CloseStillOpen(2) => - # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[2]
        08 ExpandTimedOutClose => - # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        08 ExpandNewWindow(2) => - # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        08 SetWindowUrl(1) => false # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        08 BeginExit => - # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        08 FinishExit([1]) => ReleaseOnExit(1) # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        08 AddWindow(9) => - # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        08 AdoptWindow(9) => - # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        08 AutoAddWindow(9) => - # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        08 SwapCells(0,1) => - # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        08 ReleaseWindow(2) => - # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        08 WallMinimized => - # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        08 WallRestored => - # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        08 SettleRestore => - # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        08 DeferredParkTargets([1,2]) => [] # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        08 CaptionPressStarted => void # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        08 CaptionPressEnded(true) => - # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        08 CaptionPressEnded(false) => - # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        08 AddFailed => - # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        08 ReleaseFailed(2) => - # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        08 WallActivated() => - # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        08 WallActivated(true) => - # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        08 ClickCell(0) => - # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        08 Shrink => - # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        08 WindowClosed(1) => - # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        08 SyncAlive([2]) => - # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        08 WindowMinimized(1) => - # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        08 WallResized => - # Exiting|Grid|e-|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        09 CloseCell(1) => - # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        09 CloseStillOpen(2) => - # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[2]
        09 ExpandTimedOutClose => - # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        09 ExpandNewWindow(2) => - # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        09 SetWindowUrl(1) => false # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        09 BeginExit => - # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        09 FinishExit([1]) => ReleaseOnExit(1) # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        09 AddWindow(9) => - # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        09 AdoptWindow(9) => - # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        09 AutoAddWindow(9) => - # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        09 SwapCells(0,1) => - # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        09 ReleaseWindow(2) => - # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        09 WallMinimized => - # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        09 WallRestored => - # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        09 SettleRestore => - # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        09 DeferredParkTargets([1,2]) => [] # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        09 CaptionPressStarted => void # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        09 CaptionPressEnded(true) => - # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        09 CaptionPressEnded(false) => - # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        09 AddFailed => - # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        09 ReleaseFailed(2) => - # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        09 WallActivated() => - # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        09 WallActivated(true) => - # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        09 ClickCell(0) => - # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        09 Shrink => - # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        09 WindowClosed(1) => - # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        09 SyncAlive([2]) => - # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        09 WindowMinimized(1) => - # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        09 WallResized => - # ExitingMinimized|Grid|e-|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        10 CloseCell(1) => - # Normal|Expanded|e1|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        10 CloseStillOpen(2) => Park(1),Expand(2),ShowShrinkButton # Normal|Expanded|e2|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1]|q[]
        10 ExpandTimedOutClose => - # Normal|Expanded|e1|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        10 ExpandNewWindow(2) => Park(1),Expand(2),ShowShrinkButton # Normal|Expanded|e2|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1]|q[]
        10 SetWindowUrl(1) => true # Normal|Expanded|e1|MXRAC00000|auto1|win1|free1/13|slots[W1n,W2b,Ff]|ids[1,2]|parked[2]|q[]
        10 BeginExit => CloseWindow(1),CloseWindow(2) # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        10 FinishExit([1]) => - # Normal|Expanded|e1|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        10 AddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # Normal|Expanded|e1|MXRAC00000|auto1|win1|free1/12|slots[W1a,W2b,Ff,W9g]|ids[1,2,9]|parked[2,9]|q[]
        10 AdoptWindow(9) => - # Normal|Expanded|e1|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        10 AutoAddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # Normal|Expanded|e1|MXRAC00000|auto1|win1|free1/12|slots[W1a,W2b,Ff,W9]|ids[1,2,9]|parked[2,9]|q[]
        10 SwapCells(0,1) => - # Normal|Expanded|e1|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        10 ReleaseWindow(2) => Release(2),Relayout # Normal|Expanded|e1|MXRAC00000|auto1|win1|free1/14|slots[W1a,Ff]|ids[1]|parked[]|q[]
        10 WallMinimized => Minimize(1),Minimize(2),HideShrinkButton # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        10 WallRestored => - # Normal|Expanded|e1|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        10 SettleRestore => - # Normal|Expanded|e1|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        10 DeferredParkTargets([1,2]) => [2] # Normal|Expanded|e1|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        10 CaptionPressStarted => void # Normal|Expanded|e1|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        10 CaptionPressEnded(true) => - # Normal|Expanded|e1|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        10 CaptionPressEnded(false) => - # Normal|Expanded|e1|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        10 AddFailed => Relayout # Normal|Expanded|e1|MXRAC00000|auto1|win1|free1/12|slots[W1a,W2b,Ff,Fh]|ids[1,2]|parked[2]|q[]
        10 ReleaseFailed(2) => - # Normal|Expanded|e1|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        10 WallActivated() => HideShrinkButton,ParkDeferred(1) # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        10 WallActivated(true) => - # Normal|Expanded|e1|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        10 ClickCell(0) => - # Normal|Expanded|e1|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        10 Shrink => Park(1),HideShrinkButton # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        10 WindowClosed(1) => HideShrinkButton,Relayout # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/14|slots[W2b,Ff]|ids[2]|parked[2]|q[]
        10 SyncAlive([2]) => HideShrinkButton,Relayout # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/14|slots[W2b,Ff]|ids[2]|parked[2]|q[]
        10 WindowMinimized(1) => Restore(1),Park(1),HideShrinkButton # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        10 WallResized => Park(2),Expand(1),ShowShrinkButton,Relayout # Normal|Expanded|e1|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        11 CloseCell(1) => - # Normal|Expanded|e1|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        11 CloseStillOpen(2) => - # Normal|Expanded|e1|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[2]
        11 ExpandTimedOutClose => - # Normal|Expanded|e1|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        11 ExpandNewWindow(2) => - # Normal|Expanded|e1|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        11 SetWindowUrl(1) => true # Normal|Expanded|e1|MXRAC00001|auto1|win1|free1/13|slots[W1n,W2b,Ff]|ids[1,2]|parked[2]|q[]
        11 BeginExit => CloseWindow(1),CloseWindow(2) # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        11 FinishExit([1]) => - # Normal|Expanded|e1|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        11 AddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # Normal|Expanded|e1|MXRAC00001|auto1|win1|free1/12|slots[W1a,W2b,Ff,W9g]|ids[1,2,9]|parked[2,9]|q[]
        11 AdoptWindow(9) => - # Normal|Expanded|e1|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        11 AutoAddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # Normal|Expanded|e1|MXRAC00001|auto1|win1|free1/12|slots[W1a,W2b,Ff,W9]|ids[1,2,9]|parked[2,9]|q[]
        11 SwapCells(0,1) => - # Normal|Expanded|e1|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        11 ReleaseWindow(2) => Release(2),Relayout # Normal|Expanded|e1|MXRAC00001|auto1|win1|free1/14|slots[W1a,Ff]|ids[1]|parked[]|q[]
        11 WallMinimized => Minimize(1),Minimize(2),HideShrinkButton # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        11 WallRestored => - # Normal|Expanded|e1|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        11 SettleRestore => - # Normal|Expanded|e1|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        11 DeferredParkTargets([1,2]) => [2] # Normal|Expanded|e1|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        11 CaptionPressStarted => void # Normal|Expanded|e1|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        11 CaptionPressEnded(true) => HideShrinkButton,ParkDeferred(1) # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        11 CaptionPressEnded(false) => - # Normal|Expanded|e1|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        11 AddFailed => Relayout # Normal|Expanded|e1|MXRAC00001|auto1|win1|free1/12|slots[W1a,W2b,Ff,Fh]|ids[1,2]|parked[2]|q[]
        11 ReleaseFailed(2) => - # Normal|Expanded|e1|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        11 WallActivated() => HideShrinkButton,ParkDeferred(1) # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        11 WallActivated(true) => - # Normal|Expanded|e1|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        11 ClickCell(0) => - # Normal|Expanded|e1|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        11 Shrink => Park(1),HideShrinkButton # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        11 WindowClosed(1) => HideShrinkButton,Relayout # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/14|slots[W2b,Ff]|ids[2]|parked[2]|q[]
        11 SyncAlive([2]) => HideShrinkButton,Relayout # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/14|slots[W2b,Ff]|ids[2]|parked[2]|q[]
        11 WindowMinimized(1) => Restore(1),Park(1),HideShrinkButton # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        11 WallResized => ParkDeferred(2),Relayout # ActivationParkPending|Expanded|e1|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        12 CloseCell(1) => - # ActivationParkPending|Expanded|e1|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        12 CloseStillOpen(2) => Park(1),Expand(2),ShowShrinkButton # ActivationParkPending|Expanded|e2|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1]|q[]
        12 ExpandTimedOutClose => - # ActivationParkPending|Expanded|e1|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        12 ExpandNewWindow(2) => Park(1),Expand(2),ShowShrinkButton # ActivationParkPending|Expanded|e2|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1]|q[]
        12 SetWindowUrl(1) => true # ActivationParkPending|Expanded|e1|MXRAC00010|auto1|win1|free1/13|slots[W1n,W2b,Ff]|ids[1,2]|parked[2]|q[]
        12 BeginExit => CloseWindow(1),CloseWindow(2) # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        12 FinishExit([1]) => - # ActivationParkPending|Expanded|e1|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        12 AddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # ActivationParkPending|Expanded|e1|MXRAC00010|auto1|win1|free1/12|slots[W1a,W2b,Ff,W9g]|ids[1,2,9]|parked[2,9]|q[]
        12 AdoptWindow(9) => - # ActivationParkPending|Expanded|e1|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        12 AutoAddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # ActivationParkPending|Expanded|e1|MXRAC00010|auto1|win1|free1/12|slots[W1a,W2b,Ff,W9]|ids[1,2,9]|parked[2,9]|q[]
        12 SwapCells(0,1) => - # ActivationParkPending|Expanded|e1|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        12 ReleaseWindow(2) => Release(2),Relayout # ActivationParkPending|Expanded|e1|MXRAC00010|auto1|win1|free1/14|slots[W1a,Ff]|ids[1]|parked[]|q[]
        12 WallMinimized => Minimize(1),Minimize(2),HideShrinkButton # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        12 WallRestored => - # ActivationParkPending|Expanded|e1|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        12 SettleRestore => - # ActivationParkPending|Expanded|e1|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        12 DeferredParkTargets([1,2]) => [2] # Normal|Expanded|e1|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        12 CaptionPressStarted => void # ActivationParkPending|Expanded|e1|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        12 CaptionPressEnded(true) => - # ActivationParkPending|Expanded|e1|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        12 CaptionPressEnded(false) => - # ActivationParkPending|Expanded|e1|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        12 AddFailed => Relayout # ActivationParkPending|Expanded|e1|MXRAC00010|auto1|win1|free1/12|slots[W1a,W2b,Ff,Fh]|ids[1,2]|parked[2]|q[]
        12 ReleaseFailed(2) => - # ActivationParkPending|Expanded|e1|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        12 WallActivated() => HideShrinkButton,ParkDeferred(1) # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        12 WallActivated(true) => - # ActivationParkPending|Expanded|e1|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        12 ClickCell(0) => - # ActivationParkPending|Expanded|e1|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        12 Shrink => Park(1),HideShrinkButton # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        12 WindowClosed(1) => HideShrinkButton,Relayout # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/14|slots[W2b,Ff]|ids[2]|parked[2]|q[]
        12 SyncAlive([2]) => HideShrinkButton,Relayout # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/14|slots[W2b,Ff]|ids[2]|parked[2]|q[]
        12 WindowMinimized(1) => Restore(1),Park(1),HideShrinkButton # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        12 WallResized => ParkDeferred(2),Expand(1),ShowShrinkButton,Relayout # ActivationParkPending|Expanded|e1|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        13 CloseCell(1) => - # ActivationParkPending|Expanded|e1|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        13 CloseStillOpen(2) => - # ActivationParkPending|Expanded|e1|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[2]
        13 ExpandTimedOutClose => - # ActivationParkPending|Expanded|e1|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        13 ExpandNewWindow(2) => - # ActivationParkPending|Expanded|e1|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        13 SetWindowUrl(1) => true # ActivationParkPending|Expanded|e1|MXRAC00011|auto1|win1|free1/13|slots[W1n,W2b,Ff]|ids[1,2]|parked[2]|q[]
        13 BeginExit => CloseWindow(1),CloseWindow(2) # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        13 FinishExit([1]) => - # ActivationParkPending|Expanded|e1|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        13 AddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # ActivationParkPending|Expanded|e1|MXRAC00011|auto1|win1|free1/12|slots[W1a,W2b,Ff,W9g]|ids[1,2,9]|parked[2,9]|q[]
        13 AdoptWindow(9) => - # ActivationParkPending|Expanded|e1|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        13 AutoAddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # ActivationParkPending|Expanded|e1|MXRAC00011|auto1|win1|free1/12|slots[W1a,W2b,Ff,W9]|ids[1,2,9]|parked[2,9]|q[]
        13 SwapCells(0,1) => - # ActivationParkPending|Expanded|e1|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        13 ReleaseWindow(2) => Release(2),Relayout # ActivationParkPending|Expanded|e1|MXRAC00011|auto1|win1|free1/14|slots[W1a,Ff]|ids[1]|parked[]|q[]
        13 WallMinimized => Minimize(1),Minimize(2),HideShrinkButton # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        13 WallRestored => - # ActivationParkPending|Expanded|e1|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        13 SettleRestore => - # ActivationParkPending|Expanded|e1|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        13 DeferredParkTargets([1,2]) => [2] # Normal|Expanded|e1|MXRAC00001|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        13 CaptionPressStarted => void # ActivationParkPending|Expanded|e1|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        13 CaptionPressEnded(true) => HideShrinkButton,ParkDeferred(1) # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        13 CaptionPressEnded(false) => - # ActivationParkPending|Expanded|e1|MXRAC00010|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        13 AddFailed => Relayout # ActivationParkPending|Expanded|e1|MXRAC00011|auto1|win1|free1/12|slots[W1a,W2b,Ff,Fh]|ids[1,2]|parked[2]|q[]
        13 ReleaseFailed(2) => - # ActivationParkPending|Expanded|e1|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        13 WallActivated() => HideShrinkButton,ParkDeferred(1) # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        13 WallActivated(true) => - # ActivationParkPending|Expanded|e1|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        13 ClickCell(0) => - # ActivationParkPending|Expanded|e1|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        13 Shrink => Park(1),HideShrinkButton # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        13 WindowClosed(1) => HideShrinkButton,Relayout # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/14|slots[W2b,Ff]|ids[2]|parked[2]|q[]
        13 SyncAlive([2]) => HideShrinkButton,Relayout # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/14|slots[W2b,Ff]|ids[2]|parked[2]|q[]
        13 WindowMinimized(1) => Restore(1),Park(1),HideShrinkButton # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        13 WallResized => ParkDeferred(2),Relayout # ActivationParkPending|Expanded|e1|MXRAC00011|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        14 CloseCell(1) => - # RestorePending|Expanded|e1|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        14 CloseStillOpen(2) => - # RestorePending|Expanded|e1|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[2]
        14 ExpandTimedOutClose => - # RestorePending|Expanded|e1|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        14 ExpandNewWindow(2) => - # RestorePending|Expanded|e1|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        14 SetWindowUrl(1) => true # RestorePending|Expanded|e1|MXRAC00100|auto0|win1|free1/13|slots[W1n,W2b,Ff]|ids[1,2]|parked[2]|q[]
        14 BeginExit => CloseWindow(1),CloseWindow(2) # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        14 FinishExit([1]) => - # RestorePending|Expanded|e1|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        14 AddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # RestorePending|Expanded|e1|MXRAC00100|auto0|win1|free1/12|slots[W1a,W2b,Ff,W9g]|ids[1,2,9]|parked[2,9]|q[]
        14 AdoptWindow(9) => - # RestorePending|Expanded|e1|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        14 AutoAddWindow(9) => - # RestorePending|Expanded|e1|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        14 SwapCells(0,1) => - # RestorePending|Expanded|e1|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        14 ReleaseWindow(2) => Release(2),Relayout # RestorePending|Expanded|e1|MXRAC00100|auto0|win1|free1/14|slots[W1a,Ff]|ids[1]|parked[]|q[]
        14 WallMinimized => Minimize(1),Minimize(2),HideShrinkButton # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        14 WallRestored => - # RestorePending|Expanded|e1|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        14 SettleRestore => Restore(2),Park(2),Restore(1),Expand(1),ShowShrinkButton,Relayout # Normal|Expanded|e1|MXRAC00000|auto1|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        14 DeferredParkTargets([1,2]) => [2] # RestorePending|Expanded|e1|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        14 CaptionPressStarted => void # RestorePending|Expanded|e1|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        14 CaptionPressEnded(true) => - # RestorePending|Expanded|e1|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        14 CaptionPressEnded(false) => - # RestorePending|Expanded|e1|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        14 AddFailed => Relayout # RestorePending|Expanded|e1|MXRAC00100|auto0|win1|free1/12|slots[W1a,W2b,Ff,Fh]|ids[1,2]|parked[2]|q[]
        14 ReleaseFailed(2) => - # RestorePending|Expanded|e1|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        14 WallActivated() => - # RestorePending|Expanded|e1|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        14 WallActivated(true) => - # RestorePending|Expanded|e1|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        14 ClickCell(0) => - # RestorePending|Expanded|e1|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        14 Shrink => Park(1),HideShrinkButton # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        14 WindowClosed(1) => HideShrinkButton,Relayout # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/14|slots[W2b,Ff]|ids[2]|parked[2]|q[]
        14 SyncAlive([2]) => HideShrinkButton,Relayout # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/14|slots[W2b,Ff]|ids[2]|parked[2]|q[]
        14 WindowMinimized(1) => - # RestorePending|Expanded|e1|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        14 WallResized => Relayout # RestorePending|Expanded|e1|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        15 CloseCell(1) => - # RestorePending|Expanded|e1|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        15 CloseStillOpen(2) => - # RestorePending|Expanded|e1|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[2]
        15 ExpandTimedOutClose => - # RestorePending|Expanded|e1|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        15 ExpandNewWindow(2) => - # RestorePending|Expanded|e1|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        15 SetWindowUrl(1) => true # RestorePending|Expanded|e1|MXRAC00101|auto0|win1|free1/13|slots[W1n,W2b,Ff]|ids[1,2]|parked[2]|q[]
        15 BeginExit => CloseWindow(1),CloseWindow(2) # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        15 FinishExit([1]) => - # RestorePending|Expanded|e1|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        15 AddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # RestorePending|Expanded|e1|MXRAC00101|auto0|win1|free1/12|slots[W1a,W2b,Ff,W9g]|ids[1,2,9]|parked[2,9]|q[]
        15 AdoptWindow(9) => - # RestorePending|Expanded|e1|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        15 AutoAddWindow(9) => - # RestorePending|Expanded|e1|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        15 SwapCells(0,1) => - # RestorePending|Expanded|e1|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        15 ReleaseWindow(2) => Release(2),Relayout # RestorePending|Expanded|e1|MXRAC00101|auto0|win1|free1/14|slots[W1a,Ff]|ids[1]|parked[]|q[]
        15 WallMinimized => Minimize(1),Minimize(2),HideShrinkButton # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        15 WallRestored => - # RestorePending|Expanded|e1|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        15 SettleRestore => - # RestorePending|Expanded|e1|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        15 DeferredParkTargets([1,2]) => [2] # RestorePending|Expanded|e1|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        15 CaptionPressStarted => void # RestorePending|Expanded|e1|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        15 CaptionPressEnded(true) => HideShrinkButton # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        15 CaptionPressEnded(false) => - # RestorePending|Expanded|e1|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        15 AddFailed => Relayout # RestorePending|Expanded|e1|MXRAC00101|auto0|win1|free1/12|slots[W1a,W2b,Ff,Fh]|ids[1,2]|parked[2]|q[]
        15 ReleaseFailed(2) => - # RestorePending|Expanded|e1|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        15 WallActivated() => - # RestorePending|Expanded|e1|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        15 WallActivated(true) => - # RestorePending|Expanded|e1|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        15 ClickCell(0) => - # RestorePending|Expanded|e1|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        15 Shrink => Park(1),HideShrinkButton # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[1,2]|q[]
        15 WindowClosed(1) => HideShrinkButton,Relayout # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/14|slots[W2b,Ff]|ids[2]|parked[2]|q[]
        15 SyncAlive([2]) => HideShrinkButton,Relayout # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/14|slots[W2b,Ff]|ids[2]|parked[2]|q[]
        15 WindowMinimized(1) => - # RestorePending|Expanded|e1|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        15 WallResized => Relayout # RestorePending|Expanded|e1|MXRAC00101|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        16 CloseCell(1) => - # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        16 CloseStillOpen(2) => - # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[2]
        16 ExpandTimedOutClose => - # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        16 ExpandNewWindow(2) => - # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        16 SetWindowUrl(1) => true # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1n,W2b,Ff]|ids[1,2]|parked[2]|q[]
        16 BeginExit => CloseWindow(1),CloseWindow(2) # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        16 FinishExit([1]) => - # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        16 AddWindow(9) => Minimize(9),HideTaskbarButton(9),Relayout # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/12|slots[W1a,W2b,Ff,W9g]|ids[1,2,9]|parked[2,9]|q[]
        16 AdoptWindow(9) => - # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        16 AutoAddWindow(9) => - # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        16 SwapCells(0,1) => - # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        16 ReleaseWindow(2) => - # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        16 WallMinimized => - # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        16 WallRestored => Restore(2),Park(2),Restore(1),Expand(1),ShowShrinkButton,Relayout # RestorePending|Expanded|e1|MXRAC00100|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        16 SettleRestore => - # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        16 DeferredParkTargets([1,2]) => [] # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        16 CaptionPressStarted => void # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        16 CaptionPressEnded(true) => - # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        16 CaptionPressEnded(false) => - # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        16 AddFailed => Relayout # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/12|slots[W1a,W2b,Ff,Fh]|ids[1,2]|parked[2]|q[]
        16 ReleaseFailed(2) => - # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        16 WallActivated() => - # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        16 WallActivated(true) => - # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        16 ClickCell(0) => - # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        16 Shrink => - # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        16 WindowClosed(1) => HideShrinkButton,Relayout # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/14|slots[W2b,Ff]|ids[2]|parked[2]|q[]
        16 SyncAlive([2]) => HideShrinkButton,Relayout # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/14|slots[W2b,Ff]|ids[2]|parked[2]|q[]
        16 WindowMinimized(1) => - # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        16 WallResized => - # Minimized|Expanded|e1|MXRAC10000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        17 CloseCell(1) => - # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        17 CloseStillOpen(2) => - # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[2]
        17 ExpandTimedOutClose => - # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        17 ExpandNewWindow(2) => - # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        17 SetWindowUrl(1) => false # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        17 BeginExit => - # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        17 FinishExit([1]) => ReleaseOnExit(1) # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        17 AddWindow(9) => - # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        17 AdoptWindow(9) => - # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        17 AutoAddWindow(9) => - # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        17 SwapCells(0,1) => - # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        17 ReleaseWindow(2) => - # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        17 WallMinimized => - # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        17 WallRestored => - # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        17 SettleRestore => - # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        17 DeferredParkTargets([1,2]) => [] # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        17 CaptionPressStarted => void # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        17 CaptionPressEnded(true) => - # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        17 CaptionPressEnded(false) => - # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        17 AddFailed => - # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        17 ReleaseFailed(2) => - # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        17 WallActivated() => - # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        17 WallActivated(true) => - # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        17 ClickCell(0) => - # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        17 Shrink => - # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        17 WindowClosed(1) => - # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        17 SyncAlive([2]) => - # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        17 WindowMinimized(1) => - # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        17 WallResized => - # Exiting|Expanded|e1|MXRAC01000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        18 CloseCell(1) => - # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        18 CloseStillOpen(2) => - # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[2]
        18 ExpandTimedOutClose => - # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        18 ExpandNewWindow(2) => - # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        18 SetWindowUrl(1) => false # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        18 BeginExit => - # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        18 FinishExit([1]) => ReleaseOnExit(1) # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        18 AddWindow(9) => - # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        18 AdoptWindow(9) => - # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        18 AutoAddWindow(9) => - # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        18 SwapCells(0,1) => - # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        18 ReleaseWindow(2) => - # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        18 WallMinimized => - # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        18 WallRestored => - # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        18 SettleRestore => - # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        18 DeferredParkTargets([1,2]) => [] # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        18 CaptionPressStarted => void # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        18 CaptionPressEnded(true) => - # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        18 CaptionPressEnded(false) => - # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        18 AddFailed => - # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        18 ReleaseFailed(2) => - # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        18 WallActivated() => - # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        18 WallActivated(true) => - # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        18 ClickCell(0) => - # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        18 Shrink => - # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        18 WindowClosed(1) => - # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        18 SyncAlive([2]) => - # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        18 WindowMinimized(1) => - # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        18 WallResized => - # ExitingMinimized|Expanded|e1|MXRAC11000|auto0|win1|free1/13|slots[W1a,W2b,Ff]|ids[1,2]|parked[2]|q[]
        19 CloseCell(1) => - # Normal|Empty|e-|MXRAC00000|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        19 CloseStillOpen(2) => - # Normal|Empty|e-|MXRAC00000|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        19 ExpandTimedOutClose => - # Normal|Empty|e-|MXRAC00000|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        19 ExpandNewWindow(2) => - # Normal|Empty|e-|MXRAC00000|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        19 SetWindowUrl(1) => false # Normal|Empty|e-|MXRAC00000|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        19 BeginExit => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        19 FinishExit([1]) => - # Normal|Empty|e-|MXRAC00000|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        19 AddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/15|slots[W9g]|ids[9]|parked[9]|q[]
        19 AdoptWindow(9) => Park(9),HideTaskbarButton(9),Relayout # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/15|slots[W9]|ids[9]|parked[9]|q[]
        19 AutoAddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # Normal|Grid|e-|MXRAC00000|auto1|win1|free1/15|slots[W9]|ids[9]|parked[9]|q[]
        19 SwapCells(0,1) => - # Normal|Empty|e-|MXRAC00000|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        19 ReleaseWindow(2) => - # Normal|Empty|e-|MXRAC00000|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        19 WallMinimized => - # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        19 WallRestored => - # Normal|Empty|e-|MXRAC00000|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        19 SettleRestore => - # Normal|Empty|e-|MXRAC00000|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        19 DeferredParkTargets([1,2]) => [] # Normal|Empty|e-|MXRAC00000|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        19 CaptionPressStarted => void # Normal|Empty|e-|MXRAC00000|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        19 CaptionPressEnded(true) => - # Normal|Empty|e-|MXRAC00000|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        19 CaptionPressEnded(false) => - # Normal|Empty|e-|MXRAC00000|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        19 AddFailed => Relayout # Normal|Grid|e-|MXRAC00000|auto1|win0|free1/15|slots[Fh]|ids[]|parked[]|q[]
        19 ReleaseFailed(2) => - # Normal|Empty|e-|MXRAC00000|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        19 WallActivated() => - # Normal|Empty|e-|MXRAC00000|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        19 WallActivated(true) => - # Normal|Empty|e-|MXRAC00000|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        19 ClickCell(0) => - # Normal|Empty|e-|MXRAC00000|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        19 Shrink => - # Normal|Empty|e-|MXRAC00000|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        19 WindowClosed(1) => - # Normal|Empty|e-|MXRAC00000|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        19 SyncAlive([2]) => - # Normal|Empty|e-|MXRAC00000|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        19 WindowMinimized(1) => - # Normal|Empty|e-|MXRAC00000|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        19 WallResized => ShowEmpty # Normal|Empty|e-|MXRAC00000|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        20 CloseCell(1) => - # Normal|Empty|e-|MXRAC00001|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        20 CloseStillOpen(2) => - # Normal|Empty|e-|MXRAC00001|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        20 ExpandTimedOutClose => - # Normal|Empty|e-|MXRAC00001|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        20 ExpandNewWindow(2) => - # Normal|Empty|e-|MXRAC00001|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        20 SetWindowUrl(1) => false # Normal|Empty|e-|MXRAC00001|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        20 BeginExit => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        20 FinishExit([1]) => - # Normal|Empty|e-|MXRAC00001|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        20 AddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/15|slots[W9g]|ids[9]|parked[9]|q[]
        20 AdoptWindow(9) => Park(9),HideTaskbarButton(9),Relayout # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/15|slots[W9]|ids[9]|parked[9]|q[]
        20 AutoAddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # Normal|Grid|e-|MXRAC00001|auto1|win1|free1/15|slots[W9]|ids[9]|parked[9]|q[]
        20 SwapCells(0,1) => - # Normal|Empty|e-|MXRAC00001|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        20 ReleaseWindow(2) => - # Normal|Empty|e-|MXRAC00001|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        20 WallMinimized => - # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        20 WallRestored => - # Normal|Empty|e-|MXRAC00001|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        20 SettleRestore => - # Normal|Empty|e-|MXRAC00001|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        20 DeferredParkTargets([1,2]) => [] # Normal|Empty|e-|MXRAC00001|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        20 CaptionPressStarted => void # Normal|Empty|e-|MXRAC00001|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        20 CaptionPressEnded(true) => - # Normal|Empty|e-|MXRAC00000|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        20 CaptionPressEnded(false) => - # Normal|Empty|e-|MXRAC00000|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        20 AddFailed => Relayout # Normal|Grid|e-|MXRAC00001|auto1|win0|free1/15|slots[Fh]|ids[]|parked[]|q[]
        20 ReleaseFailed(2) => - # Normal|Empty|e-|MXRAC00001|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        20 WallActivated() => - # Normal|Empty|e-|MXRAC00001|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        20 WallActivated(true) => - # Normal|Empty|e-|MXRAC00001|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        20 ClickCell(0) => - # Normal|Empty|e-|MXRAC00001|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        20 Shrink => - # Normal|Empty|e-|MXRAC00001|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        20 WindowClosed(1) => - # Normal|Empty|e-|MXRAC00001|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        20 SyncAlive([2]) => - # Normal|Empty|e-|MXRAC00001|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        20 WindowMinimized(1) => - # Normal|Empty|e-|MXRAC00001|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        20 WallResized => ShowEmpty # Normal|Empty|e-|MXRAC00001|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        21 CloseCell(1) => - # ActivationParkPending|Empty|e-|MXRAC00010|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        21 CloseStillOpen(2) => - # ActivationParkPending|Empty|e-|MXRAC00010|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        21 ExpandTimedOutClose => - # ActivationParkPending|Empty|e-|MXRAC00010|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        21 ExpandNewWindow(2) => - # ActivationParkPending|Empty|e-|MXRAC00010|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        21 SetWindowUrl(1) => false # ActivationParkPending|Empty|e-|MXRAC00010|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        21 BeginExit => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        21 FinishExit([1]) => - # ActivationParkPending|Empty|e-|MXRAC00010|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        21 AddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/15|slots[W9g]|ids[9]|parked[9]|q[]
        21 AdoptWindow(9) => Park(9),HideTaskbarButton(9),Relayout # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/15|slots[W9]|ids[9]|parked[9]|q[]
        21 AutoAddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win1|free1/15|slots[W9]|ids[9]|parked[9]|q[]
        21 SwapCells(0,1) => - # ActivationParkPending|Empty|e-|MXRAC00010|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        21 ReleaseWindow(2) => - # ActivationParkPending|Empty|e-|MXRAC00010|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        21 WallMinimized => - # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        21 WallRestored => - # ActivationParkPending|Empty|e-|MXRAC00010|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        21 SettleRestore => - # ActivationParkPending|Empty|e-|MXRAC00010|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        21 DeferredParkTargets([1,2]) => [] # Normal|Empty|e-|MXRAC00000|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        21 CaptionPressStarted => void # ActivationParkPending|Empty|e-|MXRAC00010|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        21 CaptionPressEnded(true) => - # ActivationParkPending|Empty|e-|MXRAC00010|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        21 CaptionPressEnded(false) => - # ActivationParkPending|Empty|e-|MXRAC00010|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        21 AddFailed => Relayout # ActivationParkPending|Grid|e-|MXRAC00010|auto1|win0|free1/15|slots[Fh]|ids[]|parked[]|q[]
        21 ReleaseFailed(2) => - # ActivationParkPending|Empty|e-|MXRAC00010|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        21 WallActivated() => - # ActivationParkPending|Empty|e-|MXRAC00010|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        21 WallActivated(true) => - # ActivationParkPending|Empty|e-|MXRAC00010|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        21 ClickCell(0) => - # ActivationParkPending|Empty|e-|MXRAC00010|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        21 Shrink => - # ActivationParkPending|Empty|e-|MXRAC00010|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        21 WindowClosed(1) => - # ActivationParkPending|Empty|e-|MXRAC00010|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        21 SyncAlive([2]) => - # ActivationParkPending|Empty|e-|MXRAC00010|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        21 WindowMinimized(1) => - # ActivationParkPending|Empty|e-|MXRAC00010|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        21 WallResized => ShowEmpty # ActivationParkPending|Empty|e-|MXRAC00010|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        22 CloseCell(1) => - # ActivationParkPending|Empty|e-|MXRAC00011|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        22 CloseStillOpen(2) => - # ActivationParkPending|Empty|e-|MXRAC00011|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        22 ExpandTimedOutClose => - # ActivationParkPending|Empty|e-|MXRAC00011|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        22 ExpandNewWindow(2) => - # ActivationParkPending|Empty|e-|MXRAC00011|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        22 SetWindowUrl(1) => false # ActivationParkPending|Empty|e-|MXRAC00011|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        22 BeginExit => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        22 FinishExit([1]) => - # ActivationParkPending|Empty|e-|MXRAC00011|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        22 AddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/15|slots[W9g]|ids[9]|parked[9]|q[]
        22 AdoptWindow(9) => Park(9),HideTaskbarButton(9),Relayout # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/15|slots[W9]|ids[9]|parked[9]|q[]
        22 AutoAddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win1|free1/15|slots[W9]|ids[9]|parked[9]|q[]
        22 SwapCells(0,1) => - # ActivationParkPending|Empty|e-|MXRAC00011|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        22 ReleaseWindow(2) => - # ActivationParkPending|Empty|e-|MXRAC00011|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        22 WallMinimized => - # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        22 WallRestored => - # ActivationParkPending|Empty|e-|MXRAC00011|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        22 SettleRestore => - # ActivationParkPending|Empty|e-|MXRAC00011|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        22 DeferredParkTargets([1,2]) => [] # Normal|Empty|e-|MXRAC00001|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        22 CaptionPressStarted => void # ActivationParkPending|Empty|e-|MXRAC00011|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        22 CaptionPressEnded(true) => - # ActivationParkPending|Empty|e-|MXRAC00010|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        22 CaptionPressEnded(false) => - # ActivationParkPending|Empty|e-|MXRAC00010|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        22 AddFailed => Relayout # ActivationParkPending|Grid|e-|MXRAC00011|auto1|win0|free1/15|slots[Fh]|ids[]|parked[]|q[]
        22 ReleaseFailed(2) => - # ActivationParkPending|Empty|e-|MXRAC00011|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        22 WallActivated() => - # ActivationParkPending|Empty|e-|MXRAC00011|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        22 WallActivated(true) => - # ActivationParkPending|Empty|e-|MXRAC00011|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        22 ClickCell(0) => - # ActivationParkPending|Empty|e-|MXRAC00011|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        22 Shrink => - # ActivationParkPending|Empty|e-|MXRAC00011|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        22 WindowClosed(1) => - # ActivationParkPending|Empty|e-|MXRAC00011|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        22 SyncAlive([2]) => - # ActivationParkPending|Empty|e-|MXRAC00011|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        22 WindowMinimized(1) => - # ActivationParkPending|Empty|e-|MXRAC00011|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        22 WallResized => ShowEmpty # ActivationParkPending|Empty|e-|MXRAC00011|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        23 CloseCell(1) => - # RestorePending|Empty|e-|MXRAC00100|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        23 CloseStillOpen(2) => - # RestorePending|Empty|e-|MXRAC00100|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        23 ExpandTimedOutClose => - # RestorePending|Empty|e-|MXRAC00100|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        23 ExpandNewWindow(2) => - # RestorePending|Empty|e-|MXRAC00100|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        23 SetWindowUrl(1) => false # RestorePending|Empty|e-|MXRAC00100|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        23 BeginExit => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        23 FinishExit([1]) => - # RestorePending|Empty|e-|MXRAC00100|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        23 AddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/15|slots[W9g]|ids[9]|parked[9]|q[]
        23 AdoptWindow(9) => Park(9),HideTaskbarButton(9),Relayout # RestorePending|Grid|e-|MXRAC00100|auto0|win1|free1/15|slots[W9]|ids[9]|parked[9]|q[]
        23 AutoAddWindow(9) => - # RestorePending|Empty|e-|MXRAC00100|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        23 SwapCells(0,1) => - # RestorePending|Empty|e-|MXRAC00100|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        23 ReleaseWindow(2) => - # RestorePending|Empty|e-|MXRAC00100|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        23 WallMinimized => - # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        23 WallRestored => - # RestorePending|Empty|e-|MXRAC00100|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        23 SettleRestore => ShowEmpty # Normal|Empty|e-|MXRAC00000|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        23 DeferredParkTargets([1,2]) => [] # RestorePending|Empty|e-|MXRAC00100|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        23 CaptionPressStarted => void # RestorePending|Empty|e-|MXRAC00100|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        23 CaptionPressEnded(true) => - # RestorePending|Empty|e-|MXRAC00100|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        23 CaptionPressEnded(false) => - # RestorePending|Empty|e-|MXRAC00100|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        23 AddFailed => Relayout # RestorePending|Grid|e-|MXRAC00100|auto0|win0|free1/15|slots[Fh]|ids[]|parked[]|q[]
        23 ReleaseFailed(2) => - # RestorePending|Empty|e-|MXRAC00100|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        23 WallActivated() => - # RestorePending|Empty|e-|MXRAC00100|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        23 WallActivated(true) => - # RestorePending|Empty|e-|MXRAC00100|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        23 ClickCell(0) => - # RestorePending|Empty|e-|MXRAC00100|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        23 Shrink => - # RestorePending|Empty|e-|MXRAC00100|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        23 WindowClosed(1) => - # RestorePending|Empty|e-|MXRAC00100|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        23 SyncAlive([2]) => - # RestorePending|Empty|e-|MXRAC00100|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        23 WindowMinimized(1) => - # RestorePending|Empty|e-|MXRAC00100|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        23 WallResized => ShowEmpty # RestorePending|Empty|e-|MXRAC00100|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        24 CloseCell(1) => - # RestorePending|Empty|e-|MXRAC00101|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        24 CloseStillOpen(2) => - # RestorePending|Empty|e-|MXRAC00101|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        24 ExpandTimedOutClose => - # RestorePending|Empty|e-|MXRAC00101|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        24 ExpandNewWindow(2) => - # RestorePending|Empty|e-|MXRAC00101|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        24 SetWindowUrl(1) => false # RestorePending|Empty|e-|MXRAC00101|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        24 BeginExit => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        24 FinishExit([1]) => - # RestorePending|Empty|e-|MXRAC00101|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        24 AddWindow(9) => Park(9),HideTaskbarButton(9),Relayout # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/15|slots[W9g]|ids[9]|parked[9]|q[]
        24 AdoptWindow(9) => Park(9),HideTaskbarButton(9),Relayout # RestorePending|Grid|e-|MXRAC00101|auto0|win1|free1/15|slots[W9]|ids[9]|parked[9]|q[]
        24 AutoAddWindow(9) => - # RestorePending|Empty|e-|MXRAC00101|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        24 SwapCells(0,1) => - # RestorePending|Empty|e-|MXRAC00101|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        24 ReleaseWindow(2) => - # RestorePending|Empty|e-|MXRAC00101|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        24 WallMinimized => - # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        24 WallRestored => - # RestorePending|Empty|e-|MXRAC00101|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        24 SettleRestore => - # RestorePending|Empty|e-|MXRAC00101|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        24 DeferredParkTargets([1,2]) => [] # RestorePending|Empty|e-|MXRAC00101|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        24 CaptionPressStarted => void # RestorePending|Empty|e-|MXRAC00101|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        24 CaptionPressEnded(true) => HideShrinkButton # RestorePending|Empty|e-|MXRAC00100|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        24 CaptionPressEnded(false) => - # RestorePending|Empty|e-|MXRAC00100|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        24 AddFailed => Relayout # RestorePending|Grid|e-|MXRAC00101|auto0|win0|free1/15|slots[Fh]|ids[]|parked[]|q[]
        24 ReleaseFailed(2) => - # RestorePending|Empty|e-|MXRAC00101|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        24 WallActivated() => - # RestorePending|Empty|e-|MXRAC00101|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        24 WallActivated(true) => - # RestorePending|Empty|e-|MXRAC00101|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        24 ClickCell(0) => - # RestorePending|Empty|e-|MXRAC00101|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        24 Shrink => - # RestorePending|Empty|e-|MXRAC00101|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        24 WindowClosed(1) => - # RestorePending|Empty|e-|MXRAC00101|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        24 SyncAlive([2]) => - # RestorePending|Empty|e-|MXRAC00101|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        24 WindowMinimized(1) => - # RestorePending|Empty|e-|MXRAC00101|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        24 WallResized => ShowEmpty # RestorePending|Empty|e-|MXRAC00101|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        25 CloseCell(1) => - # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        25 CloseStillOpen(2) => - # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        25 ExpandTimedOutClose => - # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        25 ExpandNewWindow(2) => - # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        25 SetWindowUrl(1) => false # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        25 BeginExit => - # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        25 FinishExit([1]) => - # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        25 AddWindow(9) => Minimize(9),HideTaskbarButton(9),Relayout # Minimized|Grid|e-|MXRAC10000|auto0|win1|free1/15|slots[W9g]|ids[9]|parked[9]|q[]
        25 AdoptWindow(9) => - # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        25 AutoAddWindow(9) => - # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        25 SwapCells(0,1) => - # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        25 ReleaseWindow(2) => - # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        25 WallMinimized => - # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        25 WallRestored => ShowEmpty # Normal|Empty|e-|MXRAC00000|auto1|win0|free1/16|slots[]|ids[]|parked[]|q[]
        25 SettleRestore => - # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        25 DeferredParkTargets([1,2]) => [] # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        25 CaptionPressStarted => void # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        25 CaptionPressEnded(true) => - # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        25 CaptionPressEnded(false) => - # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        25 AddFailed => Relayout # Minimized|Grid|e-|MXRAC10000|auto0|win0|free1/15|slots[Fh]|ids[]|parked[]|q[]
        25 ReleaseFailed(2) => - # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        25 WallActivated() => - # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        25 WallActivated(true) => - # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        25 ClickCell(0) => - # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        25 Shrink => - # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        25 WindowClosed(1) => - # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        25 SyncAlive([2]) => - # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        25 WindowMinimized(1) => - # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        25 WallResized => - # Minimized|Empty|e-|MXRAC10000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 CloseCell(1) => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 CloseStillOpen(2) => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 ExpandTimedOutClose => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 ExpandNewWindow(2) => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 SetWindowUrl(1) => false # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 BeginExit => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 FinishExit([1]) => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 AddWindow(9) => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 AdoptWindow(9) => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 AutoAddWindow(9) => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 SwapCells(0,1) => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 ReleaseWindow(2) => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 WallMinimized => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 WallRestored => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 SettleRestore => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 DeferredParkTargets([1,2]) => [] # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 CaptionPressStarted => void # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 CaptionPressEnded(true) => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 CaptionPressEnded(false) => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 AddFailed => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 ReleaseFailed(2) => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 WallActivated() => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 WallActivated(true) => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 ClickCell(0) => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 Shrink => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 WindowClosed(1) => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 SyncAlive([2]) => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 WindowMinimized(1) => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        26 WallResized => - # Exiting|Empty|e-|MXRAC01000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 CloseCell(1) => - # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 CloseStillOpen(2) => - # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 ExpandTimedOutClose => - # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 ExpandNewWindow(2) => - # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 SetWindowUrl(1) => false # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 BeginExit => - # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 FinishExit([1]) => - # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 AddWindow(9) => - # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 AdoptWindow(9) => - # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 AutoAddWindow(9) => - # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 SwapCells(0,1) => - # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 ReleaseWindow(2) => - # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 WallMinimized => - # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 WallRestored => - # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 SettleRestore => - # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 DeferredParkTargets([1,2]) => [] # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 CaptionPressStarted => void # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 CaptionPressEnded(true) => - # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 CaptionPressEnded(false) => - # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 AddFailed => - # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 ReleaseFailed(2) => - # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 WallActivated() => - # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 WallActivated(true) => - # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 ClickCell(0) => - # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 Shrink => - # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 WindowClosed(1) => - # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 SyncAlive([2]) => - # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 WindowMinimized(1) => - # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        27 WallResized => - # ExitingMinimized|Empty|e-|MXRAC11000|auto0|win0|free1/16|slots[]|ids[]|parked[]|q[]
        """;
}
