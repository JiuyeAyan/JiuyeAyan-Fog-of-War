using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SCDEFogOfWar;

internal static class NativeBuildingVisionTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Put(IntPtr manager, int id, short state, short owner, short x, short y)
    {
        IntPtr building = IntPtr.Add(manager, 0x5c + id * 0x32c);
        Marshal.WriteInt16(building, 0xd0, state);
        Marshal.WriteInt16(building, 0xd2, 3);
        Marshal.WriteInt16(building, 0xd6, owner);
        Marshal.WriteInt32(building, 0xd8, id + 10000);
        Marshal.WriteInt16(building, 0xee, x);
        Marshal.WriteInt16(building, 0xf0, y);
    }

    private static void Main()
    {
        byte[] empty = new byte[0x5c + 4000 * 0x32c];
        IntPtr manager = Marshal.AllocHGlobal(empty.Length);
        try
        {
            Marshal.Copy(empty, 0, manager, empty.Length);
            // Header counters deliberately stay zero: sparse/reused slots must not
            // be limited by active count or by a previously observed high-water mark.
            Put(manager, 0, 2, 1, 400, 400);
            Put(manager, 1, 2, 1, 10, 20);
            Put(manager, 50, 2, 2, 700, 710);
            Put(manager, 3999, 2, 8, 799, 799);
            Put(manager, 2, 3, 1, 400, 400);
            Put(manager, 3, 1, 1, 400, 400);
            Put(manager, 4, 2, 0, 400, 400);
            Put(manager, 5, 2, 9, 400, 400);
            Put(manager, 6, 2, 1, 800, 400);
            var output = new List<NativeVisionBuilding>();
            NativeBuildingVisionReader.ReadActive(manager, output);
            Check(output.Count == 3, "Invalid/inactive/reserved slots included");
            Check(output[0].Id == 1 && output[0].LogicX == 10 && output[0].LogicY == 20, "Wrong layout or axes");
            Check(output[1].Id == 50 && output[1].Owner == 2, "Far allied building missed");
            Check(output[2].Id == 3999 && output[2].Owner == 8, "Last native slot missed");

            Put(manager, 200, 2, 1, 600, 650);
            NativeBuildingVisionReader.ReadActive(manager, output);
            Check(output.Count == 4 && output[2].Id == 200, "Late script-created building missed");
            Put(manager, 50, 3, 2, 700, 710);
            Put(manager, 200, 2, 3, 100, 120);
            NativeBuildingVisionReader.ReadActive(manager, output);
            Check(output.Count == 3 && output[1].Id == 200 && output[1].Owner == 3 && output[1].LogicX == 100,
                "Deletion/slot reuse retained stale data");
            Marshal.Copy(empty, 0, manager, empty.Length);
            NativeBuildingVisionReader.ReadActive(manager, output);
            Check(output.Count == 0, "Previous match retained");

            for (int id = 1; id < 4000; id++) Put(manager, id, 2, 1, 400, 400);
            NativeBuildingVisionReader.ReadActive(manager, output);
            Check(output.Count == 3999, "Full table lost records");
            var timer = Stopwatch.StartNew();
            for (int i = 0; i < 200; i++) NativeBuildingVisionReader.ReadActive(manager, output);
            timer.Stop();
            Console.WriteLine("NATIVE_BUILDING_VISION_OK; full-table read avg {0:0.000} ms", timer.Elapsed.TotalMilliseconds / 200);
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); Environment.ExitCode = 1; }
        finally { Marshal.FreeHGlobal(manager); }
    }
}
