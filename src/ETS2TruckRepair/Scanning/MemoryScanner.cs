using System.Runtime.InteropServices;
using ETS2TruckRepair.Native;

namespace ETS2TruckRepair.Scanning;

/// <summary>
/// CE-style interactive memory scanner.
/// Scans writable memory regions for float values matching criteria,
/// then refines results with subsequent scans.
/// </summary>
public sealed class MemoryScanner
{
    private const int ChunkSize = 256 * 1024; // 256KB read chunks

    private readonly nint _processHandle;
    private List<ScanEntry> _results = [];

    public int ResultCount => _results.Count;
    public IReadOnlyList<ScanEntry> Results => _results;

    public MemoryScanner(nint processHandle)
    {
        _processHandle = processHandle;
    }

    /// <summary>
    /// First scan: find all floats in writable memory matching the predicate.
    /// </summary>
    public int FirstScan(nint scanStart, nint scanEnd, Func<float, bool> predicate)
    {
        _results.Clear();
        var address = scanStart;

        long totalScanned = 0;
        long totalRegions = 0;

        while (address < scanEnd)
        {
            var queryResult = Kernel32.VirtualQueryEx(
                _processHandle, address,
                out var memInfo,
                (nuint)Marshal.SizeOf<MemoryBasicInformation>());

            if (queryResult == 0) break;

            var regionEnd = memInfo.BaseAddress + (nint)memInfo.RegionSize;

            // Only scan committed, writable data regions
            if (memInfo.State == MemoryConstants.MemCommit && IsWritable(memInfo.Protect))
            {
                int regionSize = (int)memInfo.RegionSize;
                totalRegions++;

                ScanRegion(memInfo.BaseAddress, regionSize, predicate);
                totalScanned += regionSize;

                // Progress indicator every 100MB
                if (totalScanned % (100 * 1024 * 1024) < ChunkSize)
                {
                    Console.Write($"\r  Przeskanowano: {totalScanned / (1024 * 1024)}MB, znaleziono: {_results.Count}   ");
                }
            }

            address = regionEnd;
            if (address <= memInfo.BaseAddress) break; // overflow guard
        }

        Console.WriteLine($"\r  Skan zakonczony: {totalScanned / (1024 * 1024)}MB w {totalRegions} regionach, wynikow: {_results.Count}   ");
        return _results.Count;
    }

    /// <summary>
    /// Next scan: re-read only previously matched addresses and filter.
    /// </summary>
    public int NextScan(Func<float, float, bool> predicate)
    {
        var newResults = new List<ScanEntry>(_results.Count / 2);
        var buffer = new byte[4];

        int processed = 0;
        foreach (var entry in _results)
        {
            if (Kernel32.ReadProcessMemory(_processHandle, entry.Address, buffer, 4, out _))
            {
                float newValue = BitConverter.ToSingle(buffer);
                if (predicate(entry.Value, newValue))
                {
                    newResults.Add(new ScanEntry(entry.Address, newValue));
                }
            }

            processed++;
            if (processed % 100000 == 0)
                Console.Write($"\r  Sprawdzono: {processed}/{_results.Count}, pasuje: {newResults.Count}   ");
        }

        _results = newResults;
        Console.WriteLine($"\r  Reskan zakonczony: {_results.Count} wynikow (z {processed} sprawdzonych)   ");
        return _results.Count;
    }

    /// <summary>
    /// Groups results by proximity (addresses within maxGap bytes of each other).
    /// Damage fields are typically 5 consecutive floats (20 bytes apart).
    /// </summary>
    public List<AddressGroup> GroupByProximity(int maxGap = 64)
    {
        if (_results.Count == 0) return [];

        var sorted = _results.OrderBy(r => r.Address).ToList();
        var groups = new List<AddressGroup>();
        var currentGroup = new AddressGroup();
        currentGroup.Entries.Add(sorted[0]);

        for (int i = 1; i < sorted.Count; i++)
        {
            long gap = sorted[i].Address - sorted[i - 1].Address;
            if (gap <= maxGap)
            {
                currentGroup.Entries.Add(sorted[i]);
            }
            else
            {
                groups.Add(currentGroup);
                currentGroup = new AddressGroup();
                currentGroup.Entries.Add(sorted[i]);
            }
        }

        groups.Add(currentGroup);

        // Sort groups by size descending (most entries = most likely damage struct)
        groups.Sort((a, b) => b.Entries.Count.CompareTo(a.Entries.Count));
        return groups;
    }

    private void ScanRegion(nint baseAddr, int regionSize, Func<float, bool> predicate)
    {
        int offset = 0;

        while (offset < regionSize)
        {
            int readSize = Math.Min(ChunkSize, regionSize - offset);
            if (readSize < 4) break;

            var buffer = new byte[readSize];
            if (!Kernel32.ReadProcessMemory(_processHandle, baseAddr + offset, buffer, (nuint)readSize, out var bytesRead))
            {
                offset += ChunkSize;
                continue;
            }

            int actualRead = (int)bytesRead;

            // Scan every 4-byte aligned position for floats
            for (int i = 0; i <= actualRead - 4; i += 4)
            {
                float value = BitConverter.ToSingle(buffer, i);
                if (predicate(value))
                {
                    _results.Add(new ScanEntry(baseAddr + offset + i, value));
                }
            }

            offset += ChunkSize;
        }
    }

    private static bool IsWritable(uint protect) =>
        protect is MemoryConstants.PageReadWrite or MemoryConstants.PageExecuteReadWrite;
}

public readonly record struct ScanEntry(nint Address, float Value);

public sealed class AddressGroup
{
    public List<ScanEntry> Entries { get; } = [];

    public nint BaseAddress => Entries.Count > 0 ? Entries[0].Address : nint.Zero;

    /// <summary>
    /// Checks if this group looks like a damage struct:
    /// 3+ entries with regular intervals, all in 0-1 range.
    /// </summary>
    public bool LooksDamageStruct()
    {
        if (Entries.Count < 3 || Entries.Count > 12) return false;

        // Check for regular spacing (same gap between consecutive entries)
        var gaps = new List<long>();
        for (int i = 1; i < Entries.Count; i++)
            gaps.Add(Entries[i].Address - Entries[i - 1].Address);

        // Most common gap should appear at least 2 times
        var mostCommonGap = gaps.GroupBy(g => g).OrderByDescending(g => g.Count()).First();
        if (mostCommonGap.Count() < 2) return false;

        // All values should be in 0-1 range
        if (Entries.Any(e => e.Value < 0.0f || e.Value > 1.0f)) return false;

        return true;
    }
}
