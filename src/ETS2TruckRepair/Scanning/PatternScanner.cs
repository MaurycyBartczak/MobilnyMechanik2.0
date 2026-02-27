using System.Runtime.InteropServices;
using ETS2TruckRepair.Native;

namespace ETS2TruckRepair.Scanning;

/// <summary>
/// AOB (Array of Bytes) pattern scanner for process memory.
/// Scans executable memory regions for byte patterns with wildcard support.
/// </summary>
public sealed class PatternScanner
{
    private const int ChunkSize = 256 * 1024; // 256KB read chunks
    private readonly nint _processHandle;

    public PatternScanner(nint processHandle)
    {
        _processHandle = processHandle;
    }

    /// <summary>
    /// Parses a hex pattern string into bytes + mask.
    /// "F3 0F 10 ?? 81" → bytes=[0xF3,0x0F,0x10,0x00,0x81], mask=[true,true,true,false,true]
    /// </summary>
    public static (byte[] Bytes, bool[] Mask) ParsePattern(string pattern)
    {
        var tokens = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var bytes = new byte[tokens.Length];
        var mask = new bool[tokens.Length];

        for (int i = 0; i < tokens.Length; i++)
        {
            if (tokens[i] == "??" || tokens[i] == "?")
            {
                bytes[i] = 0;
                mask[i] = false;
            }
            else
            {
                bytes[i] = Convert.ToByte(tokens[i], 16);
                mask[i] = true;
            }
        }

        return (bytes, mask);
    }

    /// <summary>
    /// Scans executable memory regions within the given module range.
    /// Returns all match addresses, or empty list if not found.
    /// </summary>
    public List<nint> ScanModule(nint moduleBase, int moduleSize, byte[] pattern, bool[] mask)
    {
        var results = new List<nint>();
        var moduleEnd = moduleBase + moduleSize;
        var address = moduleBase;
        int overlap = pattern.Length - 1;

        while (address < moduleEnd)
        {
            var result = Kernel32.VirtualQueryEx(
                _processHandle, address,
                out var memInfo,
                (nuint)Marshal.SizeOf<MemoryBasicInformation>());

            if (result == 0) break;

            var regionEnd = memInfo.BaseAddress + (nint)memInfo.RegionSize;

            // Only scan committed, executable regions
            if (memInfo.State == MemoryConstants.MemCommit && IsExecutable(memInfo.Protect))
            {
                ScanRegion(memInfo.BaseAddress, (int)memInfo.RegionSize, pattern, mask, overlap, results);
            }

            address = regionEnd;
            if (address <= memInfo.BaseAddress) break; // overflow guard
        }

        return results;
    }

    /// <summary>
    /// Scans all committed readable/writable regions (for data scanning fallback).
    /// </summary>
    public List<nint> ScanDataRegions(nint moduleBase, int moduleSize, byte[] pattern, bool[] mask)
    {
        var results = new List<nint>();
        var moduleEnd = moduleBase + moduleSize;
        var address = moduleBase;
        int overlap = pattern.Length - 1;

        while (address < moduleEnd)
        {
            var result = Kernel32.VirtualQueryEx(
                _processHandle, address,
                out var memInfo,
                (nuint)Marshal.SizeOf<MemoryBasicInformation>());

            if (result == 0) break;

            var regionEnd = memInfo.BaseAddress + (nint)memInfo.RegionSize;

            if (memInfo.State == MemoryConstants.MemCommit && IsReadable(memInfo.Protect))
            {
                ScanRegion(memInfo.BaseAddress, (int)memInfo.RegionSize, pattern, mask, overlap, results);
            }

            address = regionEnd;
            if (address <= memInfo.BaseAddress) break;
        }

        return results;
    }

    private void ScanRegion(nint baseAddr, int regionSize, byte[] pattern, bool[] mask, int overlap, List<nint> results)
    {
        int offset = 0;

        while (offset < regionSize)
        {
            int readSize = Math.Min(ChunkSize + overlap, regionSize - offset);
            if (readSize < pattern.Length) break;

            var buffer = new byte[readSize];
            if (!Kernel32.ReadProcessMemory(_processHandle, baseAddr + offset, buffer, (nuint)readSize, out var bytesRead))
            {
                offset += ChunkSize;
                continue;
            }

            int actualRead = (int)bytesRead;
            if (actualRead < pattern.Length)
            {
                offset += ChunkSize;
                continue;
            }

            // Scan buffer for all occurrences
            int searchOffset = 0;
            while (searchOffset <= actualRead - pattern.Length)
            {
                int found = FindPattern(buffer.AsSpan(searchOffset, actualRead - searchOffset), pattern, mask);
                if (found < 0) break;

                results.Add(baseAddr + offset + searchOffset + found);
                searchOffset += found + 1; // continue past this match
            }

            offset += ChunkSize; // advance by chunk (overlap handles boundary)
        }
    }

    /// <summary>
    /// Scans a byte span for a pattern match using first-byte skip optimization.
    /// Returns offset within span, or -1 if not found.
    /// </summary>
    private static int FindPattern(ReadOnlySpan<byte> data, byte[] pattern, bool[] mask)
    {
        if (data.Length < pattern.Length) return -1;

        // Find first non-wildcard byte for fast skip
        int firstFixed = 0;
        while (firstFixed < mask.Length && !mask[firstFixed]) firstFixed++;
        if (firstFixed >= mask.Length) return 0; // all wildcards = match at 0

        byte firstByte = pattern[firstFixed];
        int end = data.Length - pattern.Length;

        for (int i = 0; i <= end; i++)
        {
            if (data[i + firstFixed] != firstByte) continue;

            bool found = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (j == firstFixed) continue; // already checked
                if (mask[j] && data[i + j] != pattern[j])
                {
                    found = false;
                    break;
                }
            }

            if (found) return i;
        }

        return -1;
    }

    private static bool IsExecutable(uint protect) =>
        protect is MemoryConstants.PageExecute or MemoryConstants.PageExecuteRead
            or MemoryConstants.PageExecuteReadWrite or MemoryConstants.PageExecuteWriteCopy;

    private static bool IsReadable(uint protect) =>
        protect is MemoryConstants.PageReadOnly or MemoryConstants.PageReadWrite
            or MemoryConstants.PageExecuteRead or MemoryConstants.PageExecuteReadWrite;
}
