using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SCDEFogOfWar
{
    internal struct NativeVisionBuilding
    {
        internal int Id;
        internal int Owner;
        internal int Type;
        internal int LogicX;
        internal int LogicY;
    }

    internal static class NativeBuildingVisionReader
    {
        // SHCDESE 5b4d48e7: GameBuildingManager.BuildingsArray at 0x5c,
        // 4000 GameBuilding records of 0x32c bytes, slot zero reserved.
        // Only called after NativeUnitVisionReader validates CrusaderDE.dll's hash.
        internal static void ReadActive(IntPtr manager, List<NativeVisionBuilding> output)
        {
            output.Clear();
            for (int id = 1; id < 4000; id++)
            {
                IntPtr building = IntPtr.Add(manager, 0x5c + id * 0x32c);
                if (Marshal.ReadInt16(building, 0xd0) != 2) continue;
                int generation = Marshal.ReadInt32(building, 0xd8);
                int owner = (ushort)Marshal.ReadInt16(building, 0xd6);
                int type = (ushort)Marshal.ReadInt16(building, 0xd2);
                int x = (ushort)Marshal.ReadInt16(building, 0xee);
                int y = (ushort)Marshal.ReadInt16(building, 0xf0);
                if (owner < 1 || owner > 8 || type <= 0 || x >= 800 || y >= 800) continue;
                // The simulation can remove/reuse slots while the render thread reads.
                // Skip visibly changing records and reconcile on the next bounded scan.
                if (Marshal.ReadInt16(building, 0xd0) != 2 ||
                    Marshal.ReadInt32(building, 0xd8) != generation ||
                    (ushort)Marshal.ReadInt16(building, 0xd6) != owner) continue;
                output.Add(new NativeVisionBuilding
                {
                    Id = id, Owner = owner, Type = type, LogicX = x, LogicY = y
                });
            }
        }
    }
}
