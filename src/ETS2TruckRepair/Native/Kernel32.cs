using System.Runtime.InteropServices;

namespace ETS2TruckRepair.Native;

[StructLayout(LayoutKind.Sequential)]
public struct MemoryBasicInformation
{
    public nint BaseAddress;
    public nint AllocationBase;
    public uint AllocationProtect;
    public ushort PartitionId;
    public nuint RegionSize;
    public uint State;
    public uint Protect;
    public uint Type;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct ProcessEntry32
{
    public uint dwSize;
    public uint cntUsage;
    public uint th32ProcessID;
    public nint th32DefaultHeapID;
    public uint th32ModuleID;
    public uint cntThreads;
    public uint th32ParentProcessID;
    public int pcPriClassBase;
    public uint dwFlags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string szExeFile;
}

/// <summary>
/// P/Invoke definitions for Windows kernel32.dll functions
/// </summary>
public static partial class Kernel32
{
    private const uint TH32CS_SNAPPROCESS = 0x00000002;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern bool Process32First(nint hSnapshot, ref ProcessEntry32 lppe);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern bool Process32Next(nint hSnapshot, ref ProcessEntry32 lppe);

    /// <summary>
    /// Finds a process by name using Toolhelp32 API (works under Wine/Proton).
    /// Returns (processId, exeName) or (0, null) if not found.
    /// </summary>
    public static (int pid, string? name) FindProcessByName(string targetName)
    {
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == nint.Zero || snapshot == (nint)(-1))
            return (0, null);

        try
        {
            var entry = new ProcessEntry32 { dwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!Process32First(snapshot, ref entry))
                return (0, null);

            do
            {
                var exeName = entry.szExeFile;
                // Strip .exe extension for comparison
                var nameWithoutExt = Path.GetFileNameWithoutExtension(exeName);
                if (nameWithoutExt.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                    return ((int)entry.th32ProcessID, exeName);
            } while (Process32Next(snapshot, ref entry));

            return (0, null);
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    /// <summary>
    /// Lists all processes using Toolhelp32 API (works under Wine/Proton).
    /// </summary>
    public static List<(int pid, string name)> ListAllProcesses()
    {
        var result = new List<(int, string)>();
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == nint.Zero || snapshot == (nint)(-1))
            return result;

        try
        {
            var entry = new ProcessEntry32 { dwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!Process32First(snapshot, ref entry))
                return result;

            do
            {
                result.Add(((int)entry.th32ProcessID, entry.szExeFile));
            } while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return result;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenProcess(
        ProcessAccessFlags dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        int dwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReadProcessMemory(
        nint hProcess,
        nint lpBaseAddress,
        [Out] byte[] lpBuffer,
        nuint nSize,
        out nuint lpNumberOfBytesRead);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WriteProcessMemory(
        nint hProcess,
        nint lpBaseAddress,
        byte[] lpBuffer,
        nuint nSize,
        out nuint lpNumberOfBytesWritten);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint hObject);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nuint VirtualQueryEx(
        nint hProcess,
        nint lpAddress,
        out MemoryBasicInformation lpBuffer,
        nuint dwLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint VirtualAllocEx(
        nint hProcess,
        nint lpAddress,
        nuint dwSize,
        uint flAllocationType,
        uint flProtect);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool VirtualFreeEx(
        nint hProcess,
        nint lpAddress,
        nuint dwSize,
        uint dwFreeType);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool VirtualProtectEx(
        nint hProcess,
        nint lpAddress,
        nuint dwSize,
        uint flNewProtect,
        out uint lpflOldProtect);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FlushInstructionCache(
        nint hProcess,
        nint lpBaseAddress,
        nuint dwSize);
}
