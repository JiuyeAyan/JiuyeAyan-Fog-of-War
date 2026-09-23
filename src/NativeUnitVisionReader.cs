using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace SCDEFogOfWar
{
    internal struct NativeVisionUnit
    {
        internal int Id;
        internal int UnitType;
        internal int Owner;
        internal float LogicX;
        internal float LogicY;
    }

    // Read-only access to the game's authoritative unit table. Unlike
    // GameMap.chimps, this table is not limited to units near the camera.
    internal sealed class NativeUnitVisionReader
    {
        private const string ModuleName = "CrusaderDE.dll";
        private const string SupportedNativeHash =
            "fbcb93195fc7efca9bdac5204852efdd76f9818f59a6711750d77c9cef2831e2";
        private const int NativeMapSize = 800;
        private const int MaximumUnitId = 10000;
        private const int UnitStride = 0x490;
        private const int LiveUnitState = 2;
        private const int UnitActiveOffset = 0x6e4;
        private const int UnitTypeOffset = 0x6e6;
        private const int UnitFineXOffset = 0x70e;
        private const int UnitFineYOffset = 0x710;
        private const int UnitCellXOffset = 0x71c;
        private const int UnitCellYOffset = 0x71e;
        // GameUnitManager + 0x65c + id * 0x490 + GameUnit.r_ControllableForPlayerId (0x92).
        // 0xa28 points at GameUnit + 0x3cc, which is not ownership.
        private const int UnitOwnerOffset = 0x6ee;
        private const long UnitManagerRva = 0x67e8400;
        private const long BuildingManagerRva = 0x64ccbb0;

        private IntPtr _unitManager;
        private IntPtr _buildingManager;
        private bool _initialized;
        private bool _permanentlyUnavailable;

        internal string FailureReason { get; private set; }

        internal bool TryReadActive(List<NativeVisionUnit> output)
        {
            if (output == null)
            {
                throw new ArgumentNullException("output");
            }
            output.Clear();
            if (!TryInitialize())
            {
                return false;
            }

            try
            {
                int count = Math.Min(
                    MaximumUnitId, Math.Max(0, Marshal.ReadInt32(_unitManager)));
                for (int id = 1; id < count; id++)
                {
                    IntPtr unit = Add(_unitManager, id * (long)UnitStride);
                    if (ReadSigned(unit, UnitActiveOffset) != LiveUnitState)
                    {
                        continue;
                    }
                    int cellX = ReadSigned(unit, UnitCellXOffset);
                    int cellY = ReadSigned(unit, UnitCellYOffset);
                    if (cellX < 0 || cellY < 0 ||
                        cellX >= NativeMapSize || cellY >= NativeMapSize)
                    {
                        continue;
                    }

                    int fineX = ReadSigned(unit, UnitFineXOffset);
                    int fineY = ReadSigned(unit, UnitFineYOffset);
                    output.Add(new NativeVisionUnit
                    {
                        Id = id,
                        UnitType = ReadUnsigned(unit, UnitTypeOffset),
                        Owner = Marshal.ReadByte(unit, UnitOwnerOffset),
                        LogicX = NativeFogCoordinates.ToCellCentre(fineX, cellX),
                        LogicY = NativeFogCoordinates.ToCellCentre(fineY, cellY)
                    });
                }
                FailureReason = null;
                return true;
            }
            catch (Exception error)
            {
                output.Clear();
                FailureReason = "native unit table read failed: " +
                    error.GetType().Name;
                return false;
            }
        }

        internal bool TryReadBuildings(List<NativeVisionBuilding> output)
        {
            output.Clear();
            if (!TryInitialize()) return false;
            try
            {
                NativeBuildingVisionReader.ReadActive(_buildingManager, output);
                FailureReason = null;
                return true;
            }
            catch (Exception error)
            {
                output.Clear();
                FailureReason = "native building table read failed: " + error.GetType().Name;
                return false;
            }
        }

        private bool TryInitialize()
        {
            if (_initialized)
            {
                return true;
            }
            if (_permanentlyUnavailable)
            {
                return false;
            }
            if (IntPtr.Size != 8)
            {
                FailureReason = "native unit table requires a 64-bit process";
                _permanentlyUnavailable = true;
                return false;
            }

            ProcessModule found = null;
            foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
            {
                if (string.Equals(
                    module.ModuleName, ModuleName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    found = module;
                    break;
                }
            }
            if (found == null)
            {
                FailureReason = "native module is not loaded yet";
                return false;
            }

            try
            {
                string hash = ComputeHash(found.FileName);
                if (!string.Equals(
                    hash, SupportedNativeHash,
                    StringComparison.OrdinalIgnoreCase))
                {
                    FailureReason = "unsupported native build " + hash;
                    _permanentlyUnavailable = true;
                    return false;
                }
                _unitManager = Add(found.BaseAddress, UnitManagerRva);
                _buildingManager = Add(found.BaseAddress, BuildingManagerRva);
                _initialized = true;
                FailureReason = null;
                return true;
            }
            catch (Exception error)
            {
                FailureReason = "native unit table initialization failed: " +
                    error.GetType().Name;
                return false;
            }
        }

        private static int ReadSigned(IntPtr address, int offset)
        {
            return Marshal.ReadInt16(address, offset);
        }

        private static int ReadUnsigned(IntPtr address, int offset)
        {
            return (ushort)Marshal.ReadInt16(address, offset);
        }

        private static string ComputeHash(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(stream))
                    .Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static IntPtr Add(IntPtr address, long offset)
        {
            return new IntPtr(address.ToInt64() + offset);
        }
    }
}
