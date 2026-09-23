using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using SCDEFogOfWar;

internal static class NativeUnitOwnerTests
{
    private static void Main()
    {
        // Independent layout fixture: manager header 0x65c, GameUnit stride 0x490.
        byte[] bytes = new byte[0x65c + 10 * 0x490];
        IntPtr manager = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, manager, bytes.Length);
            Marshal.WriteInt32(manager, 10);
            for (int id = 1; id <= 8; id++)
            {
                IntPtr unit = IntPtr.Add(manager, 0x65c + id * 0x490);
                Marshal.WriteInt16(unit, 0x88, 2);
                Marshal.WriteInt16(unit, 0x8a, 22);
                Marshal.WriteInt16(unit, 0xc0, 400);
                Marshal.WriteInt16(unit, 0xc2, 400);
                Marshal.WriteByte(unit, 0x92, (byte)id);
                Marshal.WriteByte(unit, 0x3cc, (byte)(9 - id));
            }
            var reader = new NativeUnitVisionReader();
            typeof(NativeUnitVisionReader).GetField("_unitManager", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(reader, manager);
            typeof(NativeUnitVisionReader).GetField("_initialized", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(reader, true);
            var units = new List<NativeVisionUnit>();
            if (!reader.TryReadActive(units) || units.Count != 8) throw new Exception("Active unit fixture failed");
            for (int index = 0; index < units.Count; index++)
                if (units[index].Owner != index + 1) throw new Exception("Wrong owner for unit " + units[index].Id + ": " + units[index].Owner);
            // Conversion changes type and unrelated state, not ownership.
            IntPtr first = IntPtr.Add(manager, 0x65c + 0x490);
            Marshal.WriteInt16(first, 0x8a, 3);
            Marshal.WriteByte(first, 0x3cc, 0);
            if (!reader.TryReadActive(units) || units[0].Owner != 1 || units[0].UnitType != 3) throw new Exception("Worker ownership changed");
            Marshal.WriteInt16(first, 0x88, 3);
            if (!reader.TryReadActive(units) || units.Count != 7) throw new Exception("Deleted unit retained");
            var unavailable = new NativeUnitVisionReader();
            typeof(NativeUnitVisionReader).GetField("_permanentlyUnavailable", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(unavailable, true);
            var buildings = new List<NativeVisionBuilding> { new NativeVisionBuilding { Id = 1 } };
            if (unavailable.TryReadBuildings(buildings) || buildings.Count != 0) throw new Exception("Unsupported build must not read native buildings or retain old snapshot");
            Console.WriteLine("NATIVE_UNIT_OWNER_OK");
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); Environment.ExitCode = 1; }
        finally { Marshal.FreeHGlobal(manager); }
    }
}
