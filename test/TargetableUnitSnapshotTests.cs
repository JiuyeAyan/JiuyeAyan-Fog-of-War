using System;
using SCDEFogOfWar;

internal static class TargetableUnitSnapshotTests
{
    public static int Main()
    {
        try
        {
            PublishedSnapshotIsSortedAndImmutableByReplacement();
            UnchangedSnapshotIsReusedWithoutAllocation();
            SimulationThreadFiltersWithoutLiveCollections();
            FilterPreservesPrefixDuplicatesAndInput();
            NativeCoordinatesUseCellCentres();
            RenderBoundsDoNotAccumulate();
            Console.WriteLine("FOG_TARGETABLE_SNAPSHOT_OK");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void RenderBoundsDoNotAccumulate()
    {
        int current = 200;
        int original = 200;
        int expanded = 200;
        for (int frame = 0; frame < 10000; frame++)
        {
            current = RenderBoundsPolicy.Restore(current, expanded, original);
            // Simulate the original game's early return without recomputation.
            original = current;
            current = System.Math.Max(1, current - 60);
            expanded = current;
            Assert(current == 140, "Retained bounds accumulated a second margin.");
        }
        Assert(RenderBoundsPolicy.Restore(current, expanded, original) == 200,
            "Match cleanup failed to restore native bounds.");
        Assert(RenderBoundsPolicy.Restore(250, expanded, original) == 250,
            "A new game-owned bounds value was overwritten.");
    }

    private static void NativeCoordinatesUseCellCentres()
    {
        foreach (int cell in new[] { 0, 200, 400, 799 })
        {
            Assert(NativeFogCoordinates.ToCellCentre(cell * 8 + 4, cell) == cell,
                "Native cell midpoint must match the fog cell centre.");
            Assert(NativeFogCoordinates.ToCellCentre(-1, cell) == cell,
                "Missing fine coordinate must fall back to the cell centre.");
            Assert(NativeFogCoordinates.ToCellCentre((cell + 3) * 8, cell) == cell,
                "Mismatched fine coordinate must not displace sight.");
        }
        Assert(NativeFogCoordinates.ToCellCentre(1605, 200) -
            NativeFogCoordinates.ToCellCentre(1604, 200) == 0.125f,
            "Fine movement must retain eighth-cell precision and direction.");
    }

    private static void FilterPreservesPrefixDuplicatesAndInput()
    {
        int[] visible = { 2, 4, 6 };
        int[] all = { 6, 2, 2, 4 };
        Assert(ReferenceEquals(all, TargetableUnitSnapshot.Filter(all, visible)),
            "Fully visible selection must reuse input.");
        int[] mixed = { 6, 2, 9, 2, 7, 4 };
        int[] filtered = TargetableUnitSnapshot.Filter(mixed, visible);
        Assert(filtered.Length == 4 && filtered[0] == 6 && filtered[1] == 2 &&
            filtered[2] == 2 && filtered[3] == 4 && mixed[2] == 9,
            "Lazy filtering must preserve order, duplicates, prefix and input.");
        Assert(TargetableUnitSnapshot.Filter(new[] { 9, 7 }, visible).Length == 0,
            "Fully hidden selection must be empty.");
    }

    private static void UnchangedSnapshotIsReusedWithoutAllocation()
    {
        int[] previous = { 3, 15, 17 };
        int[] unchanged = TargetableUnitSnapshot.CreateIfChanged(
            new System.Collections.Generic.List<int> { 17, 3, 15, 3 }, previous);
        Assert(ReferenceEquals(previous, unchanged),
            "An unchanged targetable snapshot allocated a replacement array.");
        int[] changed = TargetableUnitSnapshot.CreateIfChanged(
            new System.Collections.Generic.List<int> { 3, 16 }, previous);
        Assert(!ReferenceEquals(previous, changed) && changed.Length == 2 &&
               changed[0] == 3 && changed[1] == 16,
            "A changed targetable snapshot was not replaced.");
    }

    private static void PublishedSnapshotIsSortedAndImmutableByReplacement()
    {
        int[] snapshot = TargetableUnitSnapshot.Create(
            new[] { 17, 3, 15, 3 });
        Assert(snapshot.Length == 3 && snapshot[0] == 3 &&
               snapshot[1] == 15 && snapshot[2] == 17,
               "Targetable unit snapshot was not sorted and deduplicated.");
    }

    private static void SimulationThreadFiltersWithoutLiveCollections()
    {
        int[] candidates = { 3, 14, 15, 99 };
        int[] filtered = TargetableUnitSnapshot.Filter(
            candidates, new[] { 3, 15, 17 });
        Assert(filtered.Length == 2 && filtered[0] == 3 &&
               filtered[1] == 15,
               "Immutable targetable snapshot filtered the wrong units.");
        Assert(ReferenceEquals(
                   candidates,
                   TargetableUnitSnapshot.Filter(candidates, null)),
               "An unpublished snapshot blocked native selection startup.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
