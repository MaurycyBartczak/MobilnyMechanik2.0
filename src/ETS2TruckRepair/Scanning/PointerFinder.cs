using System.Runtime.InteropServices;
using ETS2TruckRepair.Memory;
using ETS2TruckRepair.Native;

namespace ETS2TruckRepair.Scanning;

/// <summary>
/// Finds stable pointer chains from module's static data to a target address.
/// These chains survive ASLR because they use offsets relative to module base.
/// </summary>
public sealed class PointerFinder
{
    private readonly ProcessMemory _memory;
    private readonly nint _processHandle;

    public PointerFinder(ProcessMemory memory)
    {
        _memory = memory;
        _processHandle = memory.ProcessHandle;
    }

    /// <summary>
    /// Result: a chain of offsets from module base.
    /// E.g. [0x1234, 0x10, 0x68] means: [[[moduleBase+0x1234] + 0x10] + 0x68] = target
    /// </summary>
    public record PointerPath(int[] Chain, nint ResolvedBase);

    /// <summary>
    /// Finds pointer paths from static module data to the target address.
    /// Depth 1: [moduleBase+staticOff] + fieldOff = target
    /// Depth 2: [[moduleBase+staticOff] + off1] + fieldOff = target
    /// </summary>
    public List<PointerPath> FindPaths(
        nint targetAddress,
        nint moduleBase,
        int moduleSize,
        int maxFieldOffset = 0x4000,
        int maxDepth = 2)
    {
        Console.WriteLine("    Skanowanie statycznych wskaznikow modulu...");

        var staticPointers = ScanStaticData(moduleBase, moduleSize);
        Console.WriteLine($"    Znaleziono {staticPointers.Count} wskaznikow statycznych.");

        var results = new List<PointerPath>();

        // Depth 1: direct pointer from static data
        foreach (var (addr, ptrVal) in staticPointers)
        {
            long diff = (long)targetAddress - (long)ptrVal;
            if (diff >= 0 && diff < maxFieldOffset)
            {
                int staticOff = (int)(addr - moduleBase);
                results.Add(new PointerPath([staticOff, (int)diff], ptrVal));
            }
        }

        if (results.Count > 0)
        {
            Console.WriteLine($"    Znaleziono {results.Count} sciezek (glebokosc 1).");
            return results;
        }

        if (maxDepth < 2) return results;

        // Depth 2: pointer -> object -> pointer -> damage
        Console.WriteLine("    Brak sciezek glebokosc 1, probuje glebokosc 2...");
        int checked2 = 0;

        foreach (var (addr, ptrVal) in staticPointers)
        {
            // Read a chunk of the object pointed to by the static pointer
            var objData = _memory.ReadBytes(ptrVal, maxFieldOffset);
            if (objData is null) continue;

            checked2++;
            if (checked2 % 500 == 0)
                Console.Write($"\r    Sprawdzono: {checked2}/{staticPointers.Count}   ");

            for (int off = 0; off <= objData.Length - 8; off += 8)
            {
                long innerPtr = BitConverter.ToInt64(objData, off);
                if (innerPtr <= 0x10000 || innerPtr > (long)0x7FFFFFFFFFFF) continue;

                long diff = (long)targetAddress - innerPtr;
                if (diff >= 0 && diff < maxFieldOffset)
                {
                    int staticOff = (int)(addr - moduleBase);
                    results.Add(new PointerPath([staticOff, off, (int)diff], (nint)innerPtr));
                }
            }
        }

        Console.WriteLine($"\r    Znaleziono {results.Count} sciezek (glebokosc 2).           ");
        return results;
    }

    /// <summary>
    /// Scans the module's writable data sections for values that look like valid pointers.
    /// </summary>
    private List<(nint Address, nint Value)> ScanStaticData(nint moduleBase, int moduleSize)
    {
        var pointers = new List<(nint, nint)>();
        var address = moduleBase;
        var moduleEnd = moduleBase + moduleSize;

        while (address < moduleEnd)
        {
            var result = Kernel32.VirtualQueryEx(
                _processHandle, address,
                out var memInfo,
                (nuint)Marshal.SizeOf<MemoryBasicInformation>());

            if (result == 0) break;

            var regionEnd = memInfo.BaseAddress + (nint)memInfo.RegionSize;

            // Only scan writable committed regions (static data, not code)
            if (memInfo.State == MemoryConstants.MemCommit && IsWritable(memInfo.Protect))
            {
                ScanRegionForPointers(memInfo.BaseAddress, (int)memInfo.RegionSize, pointers);
            }

            address = regionEnd;
            if (address <= memInfo.BaseAddress) break;
        }

        return pointers;
    }

    private void ScanRegionForPointers(nint baseAddr, int regionSize, List<(nint, nint)> pointers)
    {
        const int chunkSize = 256 * 1024;
        int offset = 0;

        while (offset < regionSize)
        {
            int readSize = Math.Min(chunkSize, regionSize - offset);
            if (readSize < 8) break;

            var buffer = new byte[readSize];
            if (!Kernel32.ReadProcessMemory(_processHandle, baseAddr + offset, buffer, (nuint)readSize, out var bytesRead))
            {
                offset += chunkSize;
                continue;
            }

            int actualRead = (int)bytesRead;

            // Look for values that look like valid user-mode x64 pointers
            for (int i = 0; i <= actualRead - 8; i += 8)
            {
                long val = BitConverter.ToInt64(buffer, i);
                // Valid user-mode pointer range on x64 Windows
                if (val > 0x10000 && val < (long)0x7FFFFFFFFFFF)
                {
                    pointers.Add((baseAddr + offset + i, (nint)val));
                }
            }

            offset += chunkSize;
        }
    }

    private static bool IsWritable(uint protect) =>
        protect is MemoryConstants.PageReadWrite or MemoryConstants.PageExecuteReadWrite;
}
